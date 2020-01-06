﻿using System.IO;
using System.Threading.Tasks;
using McMaster.AspNetCore.LetsEncrypt.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

#if NETCOREAPP2_1
using ApplicationBuilder = Microsoft.AspNetCore.Builder.Internal.ApplicationBuilder;
#endif

namespace LetsEncrypt.UnitTests
{
    public class HttpChallengeResponseMiddlewareTests
    {
        [Fact]
        public async Task ItRespondsWithTokenValue()
        {
            var services = new ServiceCollection()
                .AddLogging()
                .AddScoped<IMiddlewareFactory, MiddlewareFactory>()
                .AddLetsEncrypt()
                .Services
                .BuildServiceProvider(validateScopes: true);

            var appBuilder = new ApplicationBuilder(services);
            appBuilder.UseLetsEncryptDomainVerification();

            var app = appBuilder.Build();

            var challengeStore = services.GetRequiredService<IHttpChallengeResponseStore>();
            const string TokenValue = "abcxyz123";
            challengeStore.AddChallengeResponse("TOKEN-1", TokenValue);

            using var scope = services.CreateScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                Request =
                {
                    Path = "/.well-known/acme-challenge/TOKEN-1",
                },
                Response =
                {
                    Body = new MemoryStream(),
                }
            };

            await app.Invoke(context);

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var reader = new StreamReader(context.Response.Body);
            var streamText = reader.ReadToEnd();

            Assert.Equal(TokenValue, streamText);
            Assert.Equal("application/octet-stream", context.Response.ContentType);
            Assert.Equal(TokenValue.Length, context.Response.ContentLength);
        }

        [Fact]
        public async Task ItForwardsToNextMiddlwareForUnrecognizedChallenge()
        {
            var servicesCollection = new ServiceCollection()
                .AddLogging()
                .AddScoped<IMiddlewareFactory, MiddlewareFactory>()
                .AddLetsEncrypt()
                .Services;

            var mockChallenge = new Mock<IHttpChallengeResponseStore>();
            mockChallenge
                .Setup(s => s.TryGetResponse("unknown", out It.Ref<string>.IsAny))
                .Returns(false)
                .Verifiable();

            servicesCollection.Replace(ServiceDescriptor.Singleton(mockChallenge.Object));

            var services = servicesCollection.BuildServiceProvider(validateScopes: true);

            var appBuilder = new ApplicationBuilder(services);
            appBuilder.UseLetsEncryptDomainVerification();

            var app = appBuilder.Build();

            using var scope = services.CreateScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                Request =
                {
                    Path = "/.well-known/acme-challenge/unknown",
                },
            };

            await app.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            mockChallenge.VerifyAll();
        }
    }
}
