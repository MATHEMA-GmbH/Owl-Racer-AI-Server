using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Matlabs.OwlRacer.Server.Services;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Matlabs.OwlRacer.Common.Options;
using Matlabs.OwlRacer.Protobuf;
using Matlabs.OwlRacer.Server.Services.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Matlabs.OwlRacer.Server
{
    internal class Program
    {
        internal static async Task Main(string[] args)
        {
            var webHost = CreateHostBuilder(args).Build();

            var grpcServer = webHost.Services.GetRequiredService<IGrpcServerService>();
            grpcServer.StartServer();

            await webHost.RunAsync();

            await grpcServer.ShutdownServerAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStaticWebAssets();
                    webBuilder.UseStartup<Startup>();
                });

    }
}
 