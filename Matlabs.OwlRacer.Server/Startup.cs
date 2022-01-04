using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Matlabs.OwlRacer.Common.Options;
using Matlabs.OwlRacer.Protobuf;
using Matlabs.OwlRacer.Server.Services;
using Matlabs.OwlRacer.Server.Services.Grpc;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Matlabs.OwlRacer.Server
{
    public class Startup
    {
        private IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<GrpcCoreService.GrpcCoreServiceBase, GrpcCoreServiceImpl>();
            services.AddSingleton<GrpcResourceService.GrpcResourceServiceBase, GrpcResourceServiceImpl>();

            services.AddSingleton<IGrpcServerService, GrpcServerService>();
            services.AddSingleton<ISessionService, SessionService>();
            services.AddSingleton<IGameService, GameService>();
            services.AddSingleton<IResourceService, ResourceService>();

            services.Configure<AgentOptions>(Configuration.GetSection("Agent"));
            services.Configure<GameOptions>(Configuration.GetSection("Game"));

            services.AddCors();
            services.AddGrpc();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            //app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseCors(opt =>
            {
                opt.AllowAnyHeader();
                opt.AllowAnyMethod();
                opt.AllowAnyOrigin();
            });

            app.UseRouting();
            //app.UseGrpcWeb();

            app.UseEndpoints(endpoints =>
            {
                //endpoints.MapGrpcService<GrpcCoreServiceImpl>().EnableGrpcWeb();
                //endpoints.MapGrpcService<GrpcResourceServiceImpl>().EnableGrpcWeb();
            });
        }
    }
}
