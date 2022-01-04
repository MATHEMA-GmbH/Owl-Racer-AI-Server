using System;
using System.Threading.Tasks;
using Grpc.Core;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Matlabs.OwlRacer.Common.Options;
using Matlabs.OwlRacer.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GrpcServer = Grpc.Core.Server;

namespace Matlabs.OwlRacer.Server.Services
{
    public class GrpcServerService : IGrpcServerService
    {
        private readonly ILogger<GrpcServerService> _logger;
        private readonly GrpcServer _server;

        public GrpcServerService(
            ILogger<GrpcServerService> logger,
            IOptions<AgentOptions> agentOptions,
            GrpcCoreService.GrpcCoreServiceBase coreServiceImpl,
            GrpcResourceService.GrpcResourceServiceBase resourceServiceImpl)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var agent = agentOptions.Value;

            _logger.LogInformation($"Configuring gRPC Server to listen to {agent.Address}:{agent.Port}");
            _server = new GrpcServer()
            {
                Ports = { new ServerPort(agent.Address, agent.Port, ServerCredentials.Insecure) },
                Services =
                {
                    GrpcCoreService.BindService(coreServiceImpl),
                    GrpcResourceService.BindService(resourceServiceImpl)
                }
            };
        }

        public void StartServer()
        {
            _logger.LogInformation("Starting gRPC Server.");
            _server.Start();
        }

        public async Task ShutdownServerAsync()
        {
            _logger.LogInformation("Shutting gRPC Server down.");
            await _server.ShutdownAsync();
        }
    }
}
