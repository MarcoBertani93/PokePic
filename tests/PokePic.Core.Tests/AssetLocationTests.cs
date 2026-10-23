using System.Net;
using PokePic.Core.Assets;
using PokePic.Core.Json;

namespace PokePic.Core.Tests;

public class AssetLocationTests
{
    private static Model.CardTemplate Parse(string extra)
    {
        var result = TemplateParser.Parse(TestData.Template("", extra));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        return result.Template!;
    }

    [Fact]
    public void File_paths_are_composed_of_texts()
    {
        var template = Parse("""
            "values": {
              "text": { "BASE": "https://example.org/cards", "theme": "dark" },
              "image": { "frame": "{BASE}/{theme}/frame.png", "byLevel": "lv/{level:D3}.png", "same": "{frame}" }
            },
            """);
        var r = Resolution.BindingResolver.For(template, TestData.Sample);

        Assert.Equal(new Data.FileValue(Data.ValueKind.Image, "https://example.org/cards/dark/frame.png"), r.ValueOf("frame", "p"));
        Assert.Equal(new Data.FileValue(Data.ValueKind.Image, "lv/042.png"), r.ValueOf("byLevel", "p")); // paths can depend on the data
        Assert.Equal(r.ValueOf("frame", "p"), r.ValueOf("same", "p"));
    }

    [Fact]
    public void A_path_with_the_language_takes_the_first_file_that_exists()
    {
        var template = Parse("""
            "values": { "image": { "badge": { "pattern": "badges/{language}/{key}.png" }, "flag": "flags/{language}.png" } },
            """);
        string[] files = ["badges/en/fire.png", "badges/pt/fire.png", "flags/pt.png"];
        var r = Resolution.BindingResolver.For(template, TestData.Sample, "pt-BR", (file, _) => files.Contains(file));

        Assert.Equal("badges/pt/fire.png", Path(r));
        Assert.Equal("flags/pt.png", r.ValueOf("flag", "p").ToText());
        var english = Resolution.BindingResolver.For(template, TestData.Sample, "de", (file, _) => files.Contains(file));
        Assert.Equal("badges/en/fire.png", Path(english));

        static string Path(Resolution.BindingResolver r) =>
            r.ResolveFile(new Model.LookupExpr(new Model.SetLookup("badge", [Model.TextTemplate.FromLiteral("fire")])), Data.ValueKind.Image, "p").ToText();
    }

    [Fact]
    public void Language_candidates_are_the_language_its_base_and_english()
    {
        Assert.Equal(["pt-BR", "pt", "en"], Data.Languages.Candidates("pt-BR"));
        Assert.Equal(["EN"], Data.Languages.Candidates("EN")); // English only once
    }


    [Fact]
    public void Plain_paths_stay_in_the_template_folder_and_file_uris_can_be_absolute()
    {
        var root = Directory.CreateTempSubdirectory("pokepic-assets");
        var outside = Path.Combine(Path.GetTempPath(), $"pokepic-outside-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "a.png"), [1]);
            File.WriteAllBytes(outside, [2]);
            var assets = new FileSystemAssetProvider(root.FullName);
            var uri = new Uri(outside).AbsoluteUri; // file:///C:/…

            Assert.True(assets.Exists("a.png"));
            Assert.True(assets.Exists("file://a.png"));
            Assert.False(assets.Exists($"../{Path.GetFileName(outside)}"));
            Assert.True(assets.Exists(uri));
            Assert.Equal([2], assets.Read(uri));
        }
        finally
        {
            root.Delete(recursive: true);
            File.Delete(outside);
        }
    }

    [Fact]
    public void Web_files_are_downloaded_once_and_failures_become_issues()
    {
        var handler = new FakeHandler();
        var web = new WebAssetCache(new HttpClient(handler));
        var assets = new FileSystemAssetProvider(Path.GetTempPath(), web);

        Assert.True(assets.Exists("https://example.org/ok.png"));
        Assert.Equal([7, 7], assets.Read("https://example.org/ok.png"));
        Assert.Equal(1, handler.Requests);
        Assert.False(assets.Exists("https://example.org/missing.png"));

        var result = TemplateLoader.Load(
            TestData.Template("", """ "values": { "image": { "a": "https://example.org/ok.png", "b": "https://example.org/broken.png" } }, """),
            TestData.Sample.Schema, assets);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("values.image.b", issue.Path);
        Assert.Contains("HTTP 500", issue.Message, StringComparison.Ordinal);

        // Failures are kept until they are forgotten (F5 in the editor), then tried again.
        int before = handler.Requests;
        Assert.Throws<WebAssetException>(() => assets.Exists("https://example.org/broken.png"));
        Assert.Equal(before, handler.Requests);
        web.ForgetFailures();
        Assert.Throws<WebAssetException>(() => assets.Exists("https://example.org/broken.png"));
        Assert.Equal(before + 1, handler.Requests);
    }

    [Fact]
    public void Web_addresses_need_a_download_cache()
    {
        var assets = new FileSystemAssetProvider(Path.GetTempPath());
        Assert.Throws<WebAssetException>(() => assets.Exists("https://example.org/a.png"));
        Assert.True(assets.DirectoryExists("https://example.org/art")); // a web folder cannot be listed
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return request.RequestUri!.AbsolutePath switch
            {
                "/ok.png" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([7, 7]) },
                "/missing.png" => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
