using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;
using Opc.Ua.Bindings;
using Recon.Services;
using Serilog;
using System.Data;

public partial class Program
{
    public static Settings Settings = new() { SettingData = GlobalFunctions.LoadSetting() };
    public static List<MachineData> MachinesData = new();


    public class Startup
    {

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<ReconContext>(opt => opt.UseSqlServer(Settings.SettingData.FirstOrDefault(a => a.Key == "connectionString").Value).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddHttpContextAccessor();

            services.AddRazorPages().AddXmlSerializerFormatters().AddXmlDataContractSerializerFormatters();
            services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Basic", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "basic", In = ParameterLocation.Header, Description = "Basic Authorization header for getting Bearer Token." });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                     { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Basic" } }, new List<string>() } });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Description = "JWT Authorization header using the Bearer scheme for All safe APIs.", Name = "Authorization", In = ParameterLocation.Header, Scheme = "bearer", Type = SecuritySchemeType.Http, BearerFormat = "JWT" });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                     { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, new List<string>() } });
            });

            services.AddEndpointsApiExplorer().AddControllersWithViews();
            services.AddSingleton<IHttpContextAccessor, HtttpContextExtension>();
            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(x =>
            {
                x.BackchannelHttpHandler = new HttpClientHandler { ServerCertificateCustomValidationCallback = delegate { return true; } };
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = GlobalFunctions.ValidAndGetTokenParameters();

                x.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException)) { context.Response.Headers.Add("IS-TOKEN-EXPIRED", "true"); }
                        return Task.CompletedTask;
                    }
                };
            });
            services.AddHostedService<MachineCycleService>();
            services.AddHostedService<DataTransferService>();
            services.AddHttpContextAccessor();
            services.AddResponseCompression(options => { options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "text/javascript" }); });
            services.AddResponseCaching();
            services.AddMemoryCache();
            services.AddDistributedMemoryCache();
            services.AddSession(options =>
            {
                options.Cookie.Name = "SessionCookie";
                options.Cookie.SameSite = SameSiteMode.Lax; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.IsEssential = true; options.IdleTimeout = TimeSpan.FromMinutes(30);
            });

            services.AddEndpointsApiExplorer();
        }

        public void Configure(IApplicationBuilder app)
        {


            app.UseExceptionHandler("/Error");
            app.UseRouting();
            app.UseDefaultFiles(new DefaultFilesOptions() { DefaultFileNames = new List<string> { "index.html" } });

            app.UseHsts();
            //app.UseHttpsRedirection();


            app.UseCookiePolicy();
            app.UseSession();
            app.UseResponseCaching();
            app.UseResponseCompression();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseSwagger();
            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1"); });

            app.UseExceptionHandler("/Error");
            app.UseHsts();

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapSwagger();
                endpoints.MapControllers();
            });


            app.Use(async (HttpContext context, Func<Task> next) =>
            {
                context = GlobalFunctions.IncludeCookieTokenToRequest(context); //Include TOKEN
                await next();
            });


            app.UseStaticFiles();


        }
    }

    public static IHostBuilder BuildWebHost(string[] args)
    {
        
        return Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder => {
            webBuilder.UseStartup<Startup>();
            webBuilder.ConfigureKestrel(options => {
                options.AddServerHeader = true;
                options.ListenAnyIP(5000, opt =>
                {
                    opt.Protocols = HttpProtocols.Http1AndHttp2;
                    opt.KestrelServerOptions.AllowAlternateSchemes = true;
                });
            });
            webBuilder.UseStaticWebAssets();
            webBuilder.UseWebRoot("wwwroot");
            webBuilder.UseContentRoot(Directory.GetCurrentDirectory()); //GetCurrentDirectory For Use Razor Pages
            webBuilder.UseUrls($"http://*:5000");

        });
    }




    private static void Main(string[] args)
    {
        IHostBuilder? hostBuilder = BuildWebHost(args);
        hostBuilder.ConfigureDefaults(args).ConfigureWebHostDefaults(configure => { });
        hostBuilder.UseWindowsService(options => {
            options.ServiceName = "Recon Service";
        }).Build().Run();
    }
}