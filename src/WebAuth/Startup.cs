﻿using System;
using System.Globalization;
using AspNet.Security.OAuth.Validation;
using AutoMapper;
using Core.Application;
using Core.Settings;
using Flurl.Http;
using Lykke.SettingsReader;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAuth.Configurations;
using WebAuth.EventFilter;
using WebAuth.Providers;
using Microsoft.AspNetCore.HttpOverrides;

namespace WebAuth
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; }
        public IHostingEnvironment Environment { get; }

        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile("appsettings.dev.json", true, true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
            Environment = env;
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            OAuthSettings settings = new OAuthSettings();

            if (Environment.IsProduction() && string.IsNullOrEmpty(Configuration["SettingsUrl"]))
            {
                throw new Exception("SettingsUrl is not found");
            }

            if (string.IsNullOrEmpty(Configuration["SettingsUrl"]))
            {
                Configuration.Bind(settings);
            }
            else
            {
                settings = SettingsProcessor.Process<OAuthSettings>(Configuration["SettingsUrl"].GetStringAsync().Result);
            }

            services.AddSingleton<IOAuthSettings>(settings);

            services.AddAuthentication(options => { options.SignInScheme = "ServerCookie"; });

            services.AddLocalization(options => options.ResourcesPath = "Resources");

            services.AddCors(options =>
            {
                options.AddPolicy("Lykke", builder =>
                {
                    builder.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            services.AddMvc()
                .AddViewLocalization()
                .AddDataAnnotationsLocalization()
                .AddMvcOptions(o => { o.Filters.Add(typeof(UnhandledExceptionFilter)); });

            services.AddDistributedMemoryCache();

            services.AddAutoMapper();

            services.AddSession(options => { options.IdleTimeout = TimeSpan.FromMinutes(30); });

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            });

            WebDependencies.Create(services);

            return ApiDependencies.Create(services, settings);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            var supportedCultures = new[]
            {
                new CultureInfo("en-US"),
                new CultureInfo("en-AU"),
                new CultureInfo("en-GB"),
                new CultureInfo("en"),
                new CultureInfo("ru-RU"),
                new CultureInfo("ru"),
                new CultureInfo("fr-FR"),
                new CultureInfo("fr")
            };

            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en-GB"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            });

            app.UseCors("Lykke");

            // Create a new branch where the registered middleware will be executed only for API calls.
            app.UseOAuthValidation(new OAuthValidationOptions
            {
                AutomaticAuthenticate = true,
                AutomaticChallenge = true
            });

            // Create a new branch where the registered middleware will be executed only for non API calls.
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AutomaticAuthenticate = true,
                AutomaticChallenge = true,
                AuthenticationScheme = "ServerCookie",
                CookieName = CookieAuthenticationDefaults.CookiePrefix + "ServerCookie",
                ExpireTimeSpan = TimeSpan.FromMinutes(5),
                LoginPath = new PathString("/signin"),
                LogoutPath = new PathString("/signout")
            });

            app.UseSession();

            var applicationRepository = app.ApplicationServices.GetService<IApplicationRepository>();

            app.UseOpenIdConnectServer(options =>
            {
                options.Provider = new AuthorizationProvider(applicationRepository);

                // Enable the authorization, logout, token and userinfo endpoints.
                options.AuthorizationEndpointPath = "/connect/authorize";
                options.LogoutEndpointPath = "/connect/logout";
                options.TokenEndpointPath = "/connect/token";
                options.UserinfoEndpointPath = "/connect/userinfo";

                options.ApplicationCanDisplayErrors = true;
                options.AllowInsecureHttp = false;
            });

            app.UseCsp(options => options.DefaultSources(directive => directive.Self())
                .ImageSources(directive => directive.Self()
                    .CustomSources("*"))
                .ScriptSources(directive => directive.Self()
                    .UnsafeInline())
                .StyleSources(directive => directive.Self()
                    .UnsafeInline()));

            app.UseXContentTypeOptions();

            app.UseXfo(options => options.Deny());

            app.UseXXssProtection(options => options.EnabledWithBlockMode());

            app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");

            app.UseStaticFiles();

            app.UseMvc();
        }
    }
}
