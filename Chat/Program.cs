// dotnet add package Microsoft.SemanticKernel
using System.ClientModel;
using Azure;
using Azure.Search.Documents.Indexes;
using Chat.ModelBinders;
using Chat.Plugins;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;



#pragma warning disable SKEXP0010
namespace Chat;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        //Add Model Binder for SK AuthorRole
        builder.Services.AddControllersWithViews(options => {
            options.ModelBinderProviders.Insert(0, new AuthorRoleBinderProvider());
        }).AddRazorRuntimeCompilation();

        //Add Semantic Kernel
        var kernelBuilder = builder.Services.AddKernel();

        //Add mcp server
        await AddFileSystemMcpserver(kernelBuilder);
        await AddGitHubMcpServer(kernelBuilder, builder.Configuration.GetValue<string>("GITHUB_PAT"));

        kernelBuilder.Plugins.AddFromType<GetDateTime>();
        kernelBuilder.Plugins.AddFromType<GetWeather>();
        kernelBuilder.Plugins.AddFromType<GetGeoCoordinates>();
        kernelBuilder.Plugins.AddFromType<PersonalInfo>();

        //Add RAG Plugin
        kernelBuilder.Plugins.AddFromType<ContosoHealth>();

        //Add Azure OpenAI Service
        builder.Services.AddAzureOpenAIChatCompletion(
            deploymentName: builder.Configuration.GetValue<string>("AZURE_OPENAI_CHAT_DEPLOYMENT")!,
            endpoint: builder.Configuration.GetValue<string>("AZURE_OPENAI_ENDPOINT")!,
            apiKey: builder.Configuration.GetValue<string>("AZURE_OPENAI_KEY")!);

        // RAG: Add Text Embedding
        builder.Services.AddAzureOpenAIEmbeddingGenerator(
            deploymentName: builder.Configuration.GetValue<string>("EMBEDDING_DEPLOYNAME")!,
            endpoint: builder.Configuration.GetValue<string>("AZURE_OPENAI_ENDPOINT")!,
            apiKey: builder.Configuration.GetValue<string>("AZURE_OPENAI_KEY")!
        );

 
        //RAG: Add AI Search

        builder.Services.AddSingleton(
         sp => new SearchIndexClient(
         new Uri(builder.Configuration.GetValue<string>("AI_SEARCH_ENDPOINT")!),
         new AzureKeyCredential(builder.Configuration.GetValue<string>("AI_SEARCH_KEY")!)));

        builder.Services.AddAzureAISearchVectorStore();

        // disable concurrent invocation of functions to get the latest news and the current time
        FunctionChoiceBehaviorOptions options = new() { AllowConcurrentInvocation = false };


        builder.Services.AddTransient<PromptExecutionSettings>(_ => new OpenAIPromptExecutionSettings
        {
            Temperature = 0.75,
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: options)
        });

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}");

        app.Run();
    }

    private static async Task AddFileSystemMcpserver(IKernelBuilder kernelBuilder)
    {
        // Configure MCP client to run inside Docker
        var mcpClient = await McpClientFactory.CreateAsync(
            new StdioClientTransport(new()
            {
                Name = "FileSystem",
                Command = "docker",
                Arguments = new[]
                {
                "run",
                "-i",
                "--rm",
                // Mount your host directory into /projects inside the container
                "--mount", "type=bind,src=C:/Users/sivak/OneDrive/Documents/Reshma-Projects/Sample/modules/Chat/data,dst=/projects/data",
                "mcp/filesystem",   // Docker image name
                "/projects"         // Working directory inside container
                }
            })
        );

        try
        {
            // 🔑 Validate host directory before handshake
            var hostPath = @"C:\Users\sivak\OneDrive\Documents\Reshma-Projects\Sample\modules\Chat\data";
            if (!Directory.Exists(hostPath))
            {
                throw new DirectoryNotFoundException($"Target path does not exist: {hostPath}");
            }

            // Initialize handshake and list tools
            IList<McpClientTool> tools = await mcpClient.ListToolsAsync();

            // Register tools into Semantic Kernel
            kernelBuilder.Plugins.AddFromFunctions(
                "FS",
                tools.Select(tool => tool.AsKernelFunction())
            );
        }
        catch (Exception ex) when (ex is ArgumentException || ex is DirectoryNotFoundException)
        {
            Console.Error.WriteLine("MCP initialization failed: " + ex);
            throw;
        }
    }
    private static async Task AddGitHubMcpServer(IKernelBuilder kernelBuilder, string github_pat)
    {
        // Create an MCPClient for the GitHub server
        var mcpClient = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Name = "Github",
            Endpoint = new Uri("https://api.githubcopilot.com/mcp/"),
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {github_pat}"
            }
        }));

        // Retrieve the list of tools available on the GitHub server
        var tools = await mcpClient.ListToolsAsync();
        kernelBuilder.Plugins.AddFromFunctions("GH", tools.Select(skFunction => skFunction.AsKernelFunction()));

    }

}

