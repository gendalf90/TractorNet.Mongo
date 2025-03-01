﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace Attractor.Mongo.Tests.UseCases
{
    public class Registration
    {
        [Fact]
        public async Task Run()
        {
            // Arrange
            var testAddress = TestBytesBuffer.Generate();
            var resultChannel = Channel.CreateUnbounded<IReceivedMessageFeature>();

            using var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddAttractorServer();
                    services.RegisterActor(async (context, token) =>
                    {
                        var feature = context.Metadata.GetFeature<IReceivedMessageFeature>();

                        await resultChannel.Writer.WriteAsync(feature);
                        await feature.ConsumeAsync();
                    }, actorBuilder =>
                    {
                        actorBuilder.UseAddressPolicy(_ => testAddress);
                    });
                    services.UseMongoAddressBook(MongoClientSettings.FromConnectionString(TestMongoServer.ConnectionString), builder =>
                    {
                        // "attractor" by default
                        builder.UseDatabaseName("testAddressDb");

                        // "addressBook" by default
                        builder.UseCollectionName("testAddresses");
                    });
                    services.UseMongoMailbox(MongoClientSettings.FromConnectionString(TestMongoServer.ConnectionString), builder =>
                    {
                        // "attractor" by default
                        builder.UseDatabaseName("testMessageDb");

                        // "mailbox" by default
                        builder.UseCollectionName("testMessages");
                    });
                })
                .Build();

            // Act
            await host.StartAsync();

            var outbox = host.Services.GetRequiredService<IAnonymousOutbox>();

            var resultsMessages = new List<IReceivedMessageFeature>();

            for (int i = 0; i < 3; i++)
            {
                await outbox.SendMessageAsync(testAddress, TestBytesBuffer.CreateString($"payload {i}"));

                resultsMessages.Add(await resultChannel.Reader.ReadAsync());
            }

            await host.StopAsync();

            // Assert
            Assert.All<IAddress>(resultsMessages, resultAddress =>
            {
                Assert.True(testAddress.IsAddress(resultAddress));
            });
            Assert.Contains<IPayload>(resultsMessages, payload => TestBytesBuffer.CreatePayload(payload).IsString("payload 0"));
            Assert.Contains<IPayload>(resultsMessages, payload => TestBytesBuffer.CreatePayload(payload).IsString("payload 1"));
            Assert.Contains<IPayload>(resultsMessages, payload => TestBytesBuffer.CreatePayload(payload).IsString("payload 2"));
        }
    }
}
