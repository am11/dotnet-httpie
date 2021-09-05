using HTTPie.Abstractions;
using HTTPie.Implement;
using HTTPie.Middleware;
using HTTPie.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.CommandLine.Invocation;
using System.Text;
using WeihanLi.Common;
using WeihanLi.Common.Helpers;

namespace HTTPie.Utilities
{
    public static class Helpers
    {
        private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            HttpMethod.Head.Method,
            HttpMethod.Get.Method,
            HttpMethod.Post.Method,
            HttpMethod.Put.Method,
            HttpMethod.Patch.Method,
            HttpMethod.Delete.Method,
            HttpMethod.Options.Method
        };

        private static readonly string[] UsageExamples =
        {
            "http :5000/api/values",
            "http localhost:5000/api/values",
            "http https://reservation.weihanli.xyz/api/notice",
            "http post /api/notice title=test body=test-body"
        };

        public static readonly HashSet<Option> SupportedOptions = new();

        private static IServiceCollection AddHttpHandlerMiddleware<THttpHandlerMiddleware>(
            this IServiceCollection serviceCollection)
            where THttpHandlerMiddleware : IHttpHandlerMiddleware
        {
            serviceCollection.TryAddEnumerable(new ServiceDescriptor(typeof(IHttpHandlerMiddleware),
                typeof(THttpHandlerMiddleware), ServiceLifetime.Singleton));
            return serviceCollection;
        }


        private static IServiceCollection AddRequestMiddleware<TRequestMiddleware>(
            this IServiceCollection serviceCollection)
            where TRequestMiddleware : IRequestMiddleware
        {
            serviceCollection.TryAddEnumerable(new ServiceDescriptor(typeof(IRequestMiddleware),
                typeof(TRequestMiddleware), ServiceLifetime.Singleton));
            return serviceCollection;
        }


        private static IServiceCollection AddResponseMiddleware<TResponseMiddleware>(
            this IServiceCollection serviceCollection)
            where TResponseMiddleware : IResponseMiddleware
        {
            serviceCollection.TryAddEnumerable(new ServiceDescriptor(typeof(IResponseMiddleware),
                typeof(TResponseMiddleware), ServiceLifetime.Singleton));
            return serviceCollection;
        }

        public static void InitializeSupportOptions(IServiceProvider serviceProvider)
        {
            if (SupportedOptions.Count == 0)
            {
                foreach (var option in
                    serviceProvider.GetServices<IHttpHandlerMiddleware>()
                       .SelectMany(x => x.SupportedOptions())
                       .Union(serviceProvider.GetServices<IRequestMiddleware>()
                        .SelectMany(x => x.SupportedOptions())
                        .Union(serviceProvider.GetServices<IResponseMiddleware>()
                .SelectMany(x => x.SupportedOptions()))
                        .Union(serviceProvider.GetRequiredService<IOutputFormatter>().SupportedOptions())
                   ))
                {
                    SupportedOptions.Add(option);
                }
            }
            _command = InitializeCommand();
        }

        private static Command _command = null!;
        private static Command InitializeCommand()
        {
            var command = new RootCommand()
            {
                Name = "http",
            };
            //var methodArgument = new Argument<HttpMethod>("method")
            //{
            //    Description = "Request method",
            //    Arity = ArgumentArity.ZeroOrOne,
            //}; 
            //methodArgument.SetDefaultValue(HttpMethod.Get.Method);
            //var allowedMethods = HttpMethods.ToArray();
            //methodArgument.AddSuggestions(allowedMethods);

            //command.AddArgument(methodArgument);
            //var urlArgument = new Argument<string>("url")
            //{
            //    Description = "Request url",
            //    Arity = ArgumentArity.ExactlyOne
            //};
            //command.AddArgument(urlArgument);

            foreach (var option in SupportedOptions)
            {
                command.AddOption(option);
            }
            command.Handler = CommandHandler.Create(async (ParseResult parseResult, IConsole console) =>
            {
                var context = DependencyResolver.ResolveService<HttpContext>();
                await DependencyResolver.ResolveService<IRequestExecutor>()
                  .ExecuteAsync(context);
                var output = DependencyResolver.ResolveService<IOutputFormatter>()
                  .GetOutput(context);
                console.Out.Write(output);
            });
            command.TreatUnmatchedTokensAsErrors = false;
            return command;
        }

        // ReSharper disable once InconsistentNaming
        public static IServiceCollection RegisterHTTPieServices(this IServiceCollection serviceCollection,
            bool debugEnabled = false)
        {
            serviceCollection.AddLogging(builder =>
                    builder.AddConsole().SetMinimumLevel(debugEnabled ? LogLevel.Debug : LogLevel.Warning))
                .AddSingleton<IRequestExecutor, RequestExecutor>()
                .AddSingleton<IRequestMapper, RequestMapper>()
                .AddSingleton<IResponseMapper, ResponseMapper>()
                .AddSingleton<IOutputFormatter, OutputFormatter>()
                .AddSingleton(sp =>
                {
                    var pipelineBuilder = PipelineBuilder.CreateAsync<HttpRequestModel>();
                    foreach (var middleware in
                        sp.GetServices<IRequestMiddleware>())
                        pipelineBuilder.Use(middleware.Invoke);
                    return pipelineBuilder.Build();
                })
                .AddSingleton(sp =>
                {
                    var pipelineBuilder = PipelineBuilder.CreateAsync<HttpContext>();
                    foreach (var middleware in
                        sp.GetServices<IResponseMiddleware>())
                        pipelineBuilder.Use(middleware.Invoke);
                    return pipelineBuilder.Build();
                })
                .AddSingleton(sp =>
                {
                    var pipelineBuilder = PipelineBuilder.CreateAsync<HttpClientHandler>();
                    foreach (var middleware in
                        sp.GetServices<IHttpHandlerMiddleware>())
                        pipelineBuilder.Use(middleware.Invoke);
                    return pipelineBuilder.Build();
                })
                .AddSingleton<HttpRequestModel>()
                .AddSingleton(sp => new HttpContext(sp.GetRequiredService<HttpRequestModel>()))
                .AddSingleton<ILogger>(sp =>
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger(Constants.ApplicationName));

            // HttpHandlerMiddleware
            serviceCollection
                .AddHttpHandlerMiddleware<FollowRedirectMiddleware>()
                .AddHttpHandlerMiddleware<HttpSslMiddleware>()
                ;
            // RequestMiddleware
            serviceCollection
                .AddRequestMiddleware<QueryStringMiddleware>()
                .AddRequestMiddleware<RequestHeadersMiddleware>()
                .AddRequestMiddleware<RequestDataMiddleware>()
                .AddRequestMiddleware<DefaultRequestMiddleware>()
                .AddRequestMiddleware<AuthenticationMiddleware>()
                ;
            // ResponseMiddleware
            serviceCollection.AddResponseMiddleware<DefaultResponseMiddleware>();

            return serviceCollection;
        }

        public static void InitRequestModel(HttpContext httpContext, string commandLine)
            => InitRequestModel(httpContext, CommandLineStringSplitter.Instance.Split(commandLine).ToArray());

        public static void InitRequestModel(HttpContext httpContext, string[] args)
        {
            if(args.Contains("--help"))
            {
                return;
            }
            var requestModel = httpContext.Request;
            requestModel.ParseResult = _command.Parse(args);

            var method = requestModel.ParseResult.UnmatchedTokens.FirstOrDefault(x => HttpMethods.Contains(x));
            if (!string.IsNullOrEmpty(method))
            {
                requestModel.Method = new HttpMethod(method);
            }
            // Url
            requestModel.Url = requestModel.ParseResult.UnmatchedTokens.FirstOrDefault(x =>
                  !x.StartsWith("-", StringComparison.Ordinal)
                  && !HttpMethods.Contains(x))
                ?? string.Empty;
            if (string.IsNullOrEmpty(requestModel.Url))
            {
                throw new InvalidOperationException("The request url can not be null");
            }
            var urlIndex = Array.IndexOf(args, requestModel.Url);

            requestModel.Options = args
                .Where(x => x.StartsWith('-'))
                .ToArray();
#nullable disable
            requestModel.RequestItems = args
                .Where((x, idx) =>
                {
                    if (idx <= urlIndex)
                    {
                        return false;
                    }
                    if (string.IsNullOrEmpty(x) || x.StartsWith('-'))
                    {
                        return false;
                    }
                    var before = args[idx - 1];
                    return !(before.StartsWith('-') && before.IndexOf('=') > 0);
                })
                .Except(new[] { method, requestModel.Url })
                .ToArray();
#nullable restore
        }

        public static async Task<int> Handle(this IServiceProvider services, string[] args)
        {
            InitRequestModel(services.GetRequiredService<HttpContext>(), args);
            return await _command.InvokeAsync(args);
        }

        public static async Task<int> Handle(this IServiceProvider services, string commandLine)
        {
            InitRequestModel(services.GetRequiredService<HttpContext>(), commandLine);
            return await _command.InvokeAsync(commandLine);
        }
    }
}