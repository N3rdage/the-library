using System.Net;
using System.Text;
using System.Text.Json;
using BookTracker.Application;
using BookTracker.Application.Ai;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.Services;

// Each provider behind IAIAssistantService, driven against a canned HTTP
// response: the reading comes back parsed and one AiUsage command is sent.
[Trait("Category", TestCategories.Unit)]
public class ReadBookFromImageProviderTests
{
    private static readonly string ModelAnswer = JsonSerializer.Serialize(new
    {
        readable = true,
        title = new { value = "Collins Gem Birds", confidence = 0.9 },
        contributors = new[] { new { name = "Jim Flegg", role = "Author", confidence = 0.8 } },
        reference_work = new { is_reference_work = true, kind = "field guide", confidence = 0.7 },
    });

    private static readonly string Photo = Convert.ToBase64String([1, 2, 3]);

    private sealed class CannedHandler(string json) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static string AnthropicJson() => JsonSerializer.Serialize(new
    {
        id = "msg_1",
        type = "message",
        role = "assistant",
        model = "test-model",
        content = new[] { new { type = "text", text = ModelAnswer } },
        stop_reason = "end_turn",
        usage = new { input_tokens = 111, output_tokens = 22 },
    });

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://ai.example.test/") };

    private static void AssertReading(BookPhotoReading? reading)
    {
        Assert.NotNull(reading);
        Assert.Equal("Collins Gem Birds", reading.Title!.Value);
        Assert.Equal(new PhotoContributor("Jim Flegg", AuthorRole.Author, 0.8), Assert.Single(reading.Contributors));
        Assert.Equal(new ReferenceWorkHint(true, "field guide", 0.7), reading.ReferenceWork);
    }

    private static async Task AssertUsageSent(IDispatcher dispatcher, string provider, string model, int input, int output) =>
        await dispatcher.Received(1).Send(
            Arg.Is<RecordAiUsage>(c => c.Provider == provider && c.Model == model && c.Operation == "ReadBookFromImage"
                && c.InputTokens == input && c.OutputTokens == output),
            Arg.Any<CancellationToken>());

    [Fact]
    public async Task Anthropic_ReturnsParsedReadingAndRecordsUsage()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var handler = new CannedHandler(AnthropicJson());
        IAIAssistantService service = new AnthropicAIAssistantService(
            dispatcher, new AnthropicOptions { ApiKey = "k", FastModel = "fast-model" },
            NullLogger<AnthropicAIAssistantService>.Instance, Client(handler));

        AssertReading(await service.ReadBookFromImageAsync(Photo));

        await AssertUsageSent(dispatcher, "Anthropic", "fast-model", 111, 22);
        Assert.Contains(Photo, handler.RequestBody);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task Foundry_ReturnsParsedReadingAndRecordsUsage()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        IAIAssistantService service = new MicrosoftFoundryAIAssistantService(
            dispatcher, new MicrosoftFoundryOptions { ApiKey = "k", Endpoint = "https://ai.example.test", FastDeployment = "fast-dep" },
            NullLogger<MicrosoftFoundryAIAssistantService>.Instance, Client(new CannedHandler(AnthropicJson())));

        AssertReading(await service.ReadBookFromImageAsync(Photo));

        await AssertUsageSent(dispatcher, "MicrosoftFoundry", "fast-dep", 111, 22);
    }

    [Fact]
    public async Task AzureOpenAI_ReturnsParsedReadingAndRecordsUsage()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-1",
            @object = "chat.completion",
            created = 1,
            model = "gpt-4o",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = ModelAnswer }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 111, completion_tokens = 22, total_tokens = 133 },
        });
        var dispatcher = Substitute.For<IDispatcher>();
        IAIAssistantService service = new AzureOpenAIAssistantService(
            dispatcher, new AzureOpenAIOptions { ApiKey = "k", Endpoint = "https://ai.example.test", Deployment = "gpt-dep" },
            NullLogger<AzureOpenAIAssistantService>.Instance, Client(new CannedHandler(json)));

        AssertReading(await service.ReadBookFromImageAsync(Photo));

        await AssertUsageSent(dispatcher, "AzureOpenAI", "gpt-dep", 111, 22);
    }

    [Fact]
    public async Task Anthropic_UnreadableAnswer_ReturnsNullButStillRecordsUsage()
    {
        var json = AnthropicJson().Replace(JsonEncodedText.Encode(ModelAnswer).ToString(), "I cannot read this.");
        var dispatcher = Substitute.For<IDispatcher>();
        var service = new AnthropicAIAssistantService(
            dispatcher, new AnthropicOptions { ApiKey = "k", FastModel = "fast-model" },
            NullLogger<AnthropicAIAssistantService>.Instance, Client(new CannedHandler(json)));

        Assert.Null(await service.ReadBookFromImageAsync(Photo));

        await AssertUsageSent(dispatcher, "Anthropic", "fast-model", 111, 22);
    }
}
