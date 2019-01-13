﻿// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron HB">
//      Copyright (C) 2015-2018 Asynkron HB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Messages;
using Proto;
using Proto.Remote;
using Proto.Persistence;
using Proto.Persistence.MongoDB;
using MongoDB.Driver;
using Event = Proto.Persistence.Event;
using Snapshot = Proto.Persistence.Snapshot;

using System.Text;

using Proto.Persistence.SnapshotStrategies;
using State = Messages.State;
using RenameEvent = Messages.RenameEvent;
using RenameCommand = Messages.RenameCommand;

class Program
{
    static void Main(string[] args)
    {
		var context = new RootContext();
	
		var MongoClient = new MongoClient("mongodb://localhost:27017");
		var provider = new MongoDBProvider(MongoClient.GetDatabase("states"));

		//MongoDB.Bson.Serialization.BsonClassMap.RegisterClassMap<State>();
		var classMapp = new MongoDB.Bson.Serialization.BsonClassMap(typeof(State));
		classMapp.AutoMap();
		MongoDB.Bson.Serialization.BsonClassMap.RegisterClassMap(classMapp);

		MongoDB.Bson.Serialization.BsonClassMap.RegisterClassMap<RenameEvent>();
		MongoDB.Bson.Serialization.BsonClassMap.RegisterClassMap<RenameCommand>();

		var props = Props.FromProducer(() => new MyPersistenceActor(provider));

        var pid = context.Spawn(props);

        Console.ReadLine();
    }

    class MyPersistenceActor : IActor
    {
        private PID _loopActor;
        private State _state = new State();
        private readonly Persistence _persistence;

        public MyPersistenceActor(IProvider provider)
        {
            _persistence = Persistence.WithEventSourcingAndSnapshotting(
                provider, 
                provider, 
                "demo-app-id",
                ApplyEvent, 
                ApplySnapshot,
                new IntervalStrategy(20), () => _state);
        }

        private void ApplyEvent(Event @event)
        {
            switch (@event)
            {
                case RecoverEvent msg:
                    if(msg.Data is RenameEvent re)
                    {
                        _state.Name = re.Name;
                        Console.WriteLine("MyPersistenceActor - RecoverEvent = Event.Index = {0}, Event.Data = {1}", msg.Index, msg.Data);
                    }
                    break;
                case ReplayEvent msg:
                    if (msg.Data is RenameEvent rp)
                    {
                        _state.Name = rp.Name;
                        Console.WriteLine("MyPersistenceActor - ReplayEvent = Event.Index = {0}, Event.Data = {1}", msg.Index, msg.Data);
                    }
                    break;
                case PersistedEvent msg:
                    Console.WriteLine("MyPersistenceActor - PersistedEvent = Event.Index = {0}, Event.Data = {1}", msg.Index, msg.Data);
                    break;
            }
        }

        private void ApplySnapshot(Snapshot snapshot)
        {
            switch (snapshot)
            {
                case RecoverSnapshot msg:
                    if (msg.State is State ss)
                    {
                        _state = ss;
                        Console.WriteLine("MyPersistenceActor - RecoverSnapshot = Snapshot.Index = {0}, Snapshot.State = {1}", _persistence.Index, ss.Name);
                    }
                    break;
            }
        }

        private class StartLoopActor { }

        private bool _timerStarted = false;

        public Task ReceiveAsync(IContext context)
        {
			Task task;

            switch (context.Message)
            {
                case Started msg:
                    
                    Console.WriteLine("MyPersistenceActor - Started");

                    Console.WriteLine("MyPersistenceActor - Current State: {0}", _state);

					task = _persistence.RecoverStateAsync();
                    
                    context.Send(context.Self, new StartLoopActor());

                    break;

                case StartLoopActor msg:

					task = Handle(context, msg);

                    break;

                case RenameCommand msg:

					task = Handle(msg);

                    break;
				default:
					task = Actor.Done;
					break;
            }
			return task;
        }

        private Task Handle(IContext context, StartLoopActor message)
        {
            if (_timerStarted) return Actor.Done;

            _timerStarted = true;

            Console.WriteLine("MyPersistenceActor - StartLoopActor");

            var props = Props.FromProducer(() => new LoopActor());

            _loopActor = context.Spawn(props);
            
            return Actor.Done;
        }

        private Task Handle(RenameCommand message)
        {
            Console.WriteLine("MyPersistenceActor - RenameCommand");

            _state.Name = message.Name;

            return _persistence.PersistEventAsync(new RenameEvent { Name = message.Name });
        }
    }

    class LoopActor : IActor
    {
        private class LoopParentMessage { }

        public Task ReceiveAsync(IContext context)
        {
            switch(context.Message)
            {
                case Started _:

                    Console.WriteLine("LoopActor - Started");

                    context.Send(context.Self, new LoopParentMessage());

                    break;
                case LoopParentMessage _:

                    Task.Run(async () => {
                        
                        context.Send(context.Parent, new RenameCommand { Name = GeneratePronounceableName(5) });

                        await Task.Delay(TimeSpan.FromMilliseconds(500));

                        context.Send(context.Self ,new LoopParentMessage());
                    });

                    break;
            }

            return Actor.Done;
        }

        static string GeneratePronounceableName(int length)
        {
            const string vowels = "aeiou";
            const string consonants = "bcdfghjklmnpqrstvwxyz";

            var rnd = new Random();
            var name = new StringBuilder();

            length = length % 2 == 0 ? length : length + 1;

            for (var i = 0; i < length / 2; i++)
            {
                name
                    .Append(vowels[rnd.Next(vowels.Length)])
                    .Append(consonants[rnd.Next(consonants.Length)]);
            }

            return name.ToString();
        }
    }
}