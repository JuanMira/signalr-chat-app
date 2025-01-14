﻿using System;
using System.Linq;
using System.Reflection;
using IdentityServer.Data;
using IdentityServer.Infrastructure.Settings;
using IdentityServer.Services;
using IdentityServer4.EntityFramework.DbContexts;
using IdentityServer4.EntityFramework.Mappers;
using IdentityServerWithAspNetIdentity.Data;
using IdentityServerWithAspNetIdentity.Models;
using IdentityServerWithAspNetIdentity.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication;
using identityserver.Infrastructure.Settings;

namespace IdentityServerWithAspNetIdentity
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        readonly string AllowAnyOrigin = "_allowAnyOrigin";

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<ApplicationDbContext>(options => options.UseMySql(connectionString));

            //Add a DbContext to store your Database Keys
            services.AddDbContext<MyKeysContext>(options => options.UseMySql(connectionString));

            // using Microsoft.AspNetCore.DataProtection;
            services.AddDataProtection()
                .PersistKeysToDbContext<MyKeysContext>();

            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            // Add application services.
            services.AddTransient<IEmailSender, EmailSender>();
            services.AddTransient<IChatService, ChatService>();
            services.Configure<ClientConfigs>(Configuration.GetSection("ClientConfigs"));
            services.Configure<ExternalAuthenticationSettings>(Configuration.GetSection("ExternalAuthenticationSettings"));
            services.AddMvc();

            // configure identity server with in-memory stores, keys, clients and scopes

            var migrationsAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;
            var issuerUri = Configuration.GetSection("IssuerUri").Get<string>();

            services.AddIdentityServer(options =>
                {
                    options.IssuerUri = issuerUri;
                    options.PublicOrigin = options.IssuerUri;
                })
                .AddDeveloperSigningCredential()
                .AddAspNetIdentity<ApplicationUser>()
                .AddProfileService<IdentityProfileService>()
                .AddConfigurationStore(options =>
                {
                    options.ConfigureDbContext = b => b.UseMySql(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
                })
                .AddOperationalStore(options =>
                {
                    options.ConfigureDbContext = b => b.UseMySql(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
                    options.EnableTokenCleanup = true;
                });

            var extenalAuthenOptions = Configuration.GetSection("ExternalAuthenticationSettings").Get<ExternalAuthenticationSettings>();

            if (!Configuration.GetSection("IsDevelopment").Get<bool>())
            {
                services.AddAuthentication()
                    .AddGoogle(options =>
                    {
                        options.ClientId = extenalAuthenOptions.GoogleClientId;
                        options.ClientSecret = extenalAuthenOptions.GoogleClientSecret;
                    })
                    .AddFacebook(options =>
                    {
                        options.AppId = extenalAuthenOptions.FbClientId;
                        options.ClientSecret = extenalAuthenOptions.FbClientSecret;
                        options.AccessDeniedPath = "/Account/AccessDenied";
                    });
            }

            services.AddCors(options =>
            {
                // this defines a CORS policy called "default"
                options.AddPolicy(AllowAnyOrigin, policy =>
                {
                    policy.AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                });
            });

            services.AddHttpClient("ChatApp", c =>
            {
                var clients = Configuration.GetSection("NamedHttpClientFactories").Get<NamedHttpClientFactories[]>();
                c.BaseAddress = new Uri(clients.First().BaseAddress);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }
            
            // Enable it for the first time
            // InitializeDatabase(app);

            app.UseCookiePolicy(new CookiePolicyOptions
            {
                MinimumSameSitePolicy = SameSiteMode.Lax
            });

            app.UseCors(AllowAnyOrigin);
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();
            app.UseIdentityServer();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(name: "default", pattern: "{controller=Home}/{Action=Index}/{id?}");
                endpoints.MapRazorPages();
            });
        }

        private void InitializeDatabase(IApplicationBuilder app)
        {
            using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                serviceScope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

                var context = serviceScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
                context.Database.Migrate();
                if (!context.Clients.Any())
                {
                    foreach (var client in Config.GetClients())
                    {
                        context.Clients.Add(client.ToEntity());
                    }
                    context.SaveChanges();
                }

                if (!context.IdentityResources.Any())
                {
                    foreach (var resource in Config.GetIdentityResources())
                    {
                        context.IdentityResources.Add(resource.ToEntity());
                    }
                    context.SaveChanges();
                }

                if (!context.ApiResources.Any())
                {
                    foreach (var resource in Config.GetApiResources())
                    {
                        context.ApiResources.Add(resource.ToEntity());
                    }
                    context.SaveChanges();
                }
            }
        }
    }
}
