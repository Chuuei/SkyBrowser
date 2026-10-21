using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkyBrowser.Core.Astronomy;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class SurveyTests
{
    [Theory]
    [InlineData("survey='unknown'")]
    [InlineData("dec=91")]
    [InlineData("size=4294967424")]
    [InlineData("ra='invalid'")]
    [InlineData("key='wrong-key'")]
    public async Task InvalidCachedMetadataDoesNotHideValidOfflineRegions(string damage)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var valid = new SurveyRequest(SurveyDefinition.Color.Id, new(10, 40), 2, 128);
            var damaged = valid with { Center = new(20, 40) };
            var fixtures = new[] { valid, damaged }.ToDictionary(request => request.DownloadUri,
                request => FitsFixture(directory, request));
            var handler = new Handler((message, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fixtures[message.RequestUri!])
            }));
            using var client = new HttpClient(handler);
            var service = new SurveyImageService(Path.Combine(directory, "cache"), client);
            await service.LoadAsync(valid, false);
            await service.LoadAsync(damaged, false);
            using (var connection = new SqliteConnection($"Data Source={service.DatabasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"UPDATE frames SET {damage} WHERE key=$key";
                command.Parameters.AddWithValue("$key", damaged.Key);
                command.ExecuteNonQuery();
            }

            Assert.Equal(valid, Assert.Single(service.GetCached()).Request);
            var restored = Assert.Single(await service.LoadCachedAsync());
            Assert.Equal(valid, restored.Request);
            Assert.True(restored.FromCache);
            Assert.Equal(2, handler.Calls);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RestoringAllCachedRegionsStaysOfflinePreservesOrderAndSkipsDamagedEntries()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var requests = new[]
            {
                new SurveyRequest(SurveyDefinition.Color.Id, new(0, 0), 4, 128),
                new SurveyRequest(SurveyDefinition.Red.Id, new(90, 0), 4, 128),
                new SurveyRequest(SurveyDefinition.Color.Id, new(180, 0), 4, 128)
            };
            var fixtures = requests.ToDictionary(request => request.DownloadUri, request => FitsFixture(directory, request));
            var handler = new Handler((message, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fixtures[message.RequestUri!])
            }));
            using var client = new HttpClient(handler);
            string cacheDirectory = Path.Combine(directory, "cache");
            var service = new SurveyImageService(cacheDirectory, client);
            Assert.Empty(await service.LoadCachedAsync());
            Assert.Equal(0, handler.Calls);
            foreach (var request in requests) { await service.LoadAsync(request, false); }
            using (var connection = new SqliteConnection($"Data Source={service.DatabasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE frames SET data=x'0001' WHERE key=$key";
                command.Parameters.AddWithValue("$key", requests[1].Key);
                command.ExecuteNonQuery();
            }
            var expected = service.GetCached().Select(entry => entry.Request).Where(request => request != requests[1]).ToArray();
            var noNetwork = new Handler((_, _) => throw new InvalidOperationException("Cache restoration must never connect."));
            using var offline = new HttpClient(noNetwork);
            var reopened = new SurveyImageService(cacheDirectory, offline);
            var frames = await reopened.LoadCachedAsync();
            Assert.Equal(expected, frames.Select(frame => frame.Request));
            Assert.All(frames, frame => Assert.True(frame.FromCache));
            Assert.Equal(expected, reopened.GetCached().Select(entry => entry.Request));
            Assert.Equal(0, noNetwork.Calls);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reopened.LoadCachedAsync(cancelled.Token));
        }
        finally { Directory.Delete(directory, true); }
    }

    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls
        {
            get; private set;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    internal static byte[] FitsFixture(string directory, SurveyRequest request)
    {
        int size = request.Size;
        byte[] pixels = new byte[size * size * 3];
        for (int i = 0; i < size * size; i++)
        {
            pixels[i] = 120;
            pixels[size * size + i] = (byte)(30 + i % 100);
            pixels[2 * size * size + i] = 60;
        }
        var image = XisfImageData.FromSamples(pixels, [size, size], 3);
        double scale = 2 * Math.Tan(request.FieldOfView * SkyCoordinate.Radians / 2) / (size * SkyCoordinate.Radians);
        void Key(string name, object value) => image.FitsKeywords.Add(XisfFitsKeyword.FromValue(name, value));
        Key("CTYPE1", "RA---TAN");
        Key("CTYPE2", "DEC--TAN");
        Key("RADESYS", "ICRS");
        Key("CRVAL1", request.Center.RightAscension);
        Key("CRVAL2", request.Center.Declination);
        Key("CRPIX1", (size + 1) / 2d);
        Key("CRPIX2", (size + 1) / 2d);
        Key("CDELT1", -scale);
        Key("CDELT2", scale);
        Key("LATPOLE", request.Center.Declination);
        string path = Path.Combine(directory, "survey-fixture.fits");
        FitsWriter.Write(path, FitsConverter.FromXisf([image], ConversionLossPolicy.Allow));
        return File.ReadAllBytes(path);
    }

    [Fact]
    public async Task SurveyDownloadsValidateAstrometryAndReopenOfflineWithoutAnyNetwork()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var request = new SurveyRequest(SurveyDefinition.Color.Id, new(359.99, 89.5), 3, 128);
            byte[] fits = FitsFixture(directory, request);
            var handler = new Handler((message, _) =>
            {
                Assert.Equal(request.DownloadUri, message.RequestUri);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fits) });
            });
            using var client = new HttpClient(handler);
            var service = new SurveyImageService(Path.Combine(directory, "cache"), client);
            var loaded = await service.LoadAsync(request, false);
            Assert.False(loaded.FromCache);
            Assert.Equal(1, handler.Calls);
            Assert.InRange(loaded.Image.Placement!.Center.DistanceTo(request.Center), 0, 1e-8);
            Assert.InRange(Math.Abs(loaded.Image.Placement.AngularWidth - request.FieldOfView), 0, 1e-8);
            Assert.Empty(loaded.Image.Details.Entries);
            Assert.Single(service.GetCached());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(service.DatabasePath)!, ".survey-*.fits"));
            using var offlineClient = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Offline must never connect.")));
            var reopened = new SurveyImageService(Path.Combine(directory, "cache"), offlineClient);
            var cached = await reopened.LoadAsync(request, true);
            Assert.True(cached.FromCache);
            Assert.Equal(loaded.Image.Preview.Bgra, cached.Image.Preview.Bgra);
            Assert.Contains("STScI/NASA", cached.Image.Metadata);
            Assert.Contains("CDS", cached.Image.Metadata);
            await Assert.ThrowsAsync<FileNotFoundException>(() => reopened.LoadAsync(request with { FieldOfView = 4 }, true));
            reopened.ClearCache();
            Assert.Empty(service.GetCached());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidOversizedAndCancelledResponsesNeverReplaceSavedData()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var request = new SurveyRequest(SurveyDefinition.Red.Id, new(10, 40), 2, 128);
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("<html>Service error</html>"))
            }));
            using var client = new HttpClient(handler);
            var service = new SurveyImageService(Path.Combine(directory, "cache"), client);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(request, false));
            Assert.Empty(service.GetCached());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(request, false, cancelled.Token));
            Assert.Equal(1, handler.Calls);
            var largeHandler = new Handler((_, _) =>
            {
                var content = new ByteArrayContent([1, 2, 3]);
                content.Headers.ContentLength = SurveyImageService.MaximumResponseBytes + 1L;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            });
            using var largeClient = new HttpClient(largeHandler);
            var large = new SurveyImageService(Path.Combine(directory, "cache"), largeClient);
            await Assert.ThrowsAsync<InvalidDataException>(() => large.LoadAsync(request, false));
            Assert.Empty(large.GetCached());
            byte[] wrongSize = FitsFixture(directory, request);
            using var wrongClient = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(wrongSize)
            })));
            var wrong = new SurveyImageService(Path.Combine(directory, "cache"), wrongClient);
            await Assert.ThrowsAsync<InvalidDataException>(() => wrong.LoadAsync(request with { Size = 2048 }, false));
            Assert.Empty(wrong.GetCached());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void SurveyRequestSpecifiesCenteredJ2000TanAndCannotUseArbitraryServers()
    {
        var request = new SurveyRequest(SurveyDefinition.Color.Id, new(360, 90), 4, 512);
        Assert.Equal("alasky.cds.unistra.fr", request.DownloadUri.Host);
        string query = Uri.UnescapeDataString(request.DownloadUri.Query);
        string json = query[(query.IndexOf("wcs=", StringComparison.Ordinal) + 4)..];
        using var wcs = JsonDocument.Parse(json);
        Assert.Equal(0, wcs.RootElement.GetProperty("CRVAL1").GetDouble());
        Assert.Equal(256.5, wcs.RootElement.GetProperty("CRPIX1").GetDouble());
        Assert.True(wcs.RootElement.GetProperty("CDELT1").GetDouble() < 0);
        Assert.True(wcs.RootElement.GetProperty("CDELT2").GetDouble() > 0);
        Assert.Equal(request.Key, (request with
        {
            Center = new(0, 90)
        }).Key);
        Assert.Throws<ArgumentException>(() => (request with { SurveyId = "https://other.invalid" }).Validate());
    }

    [Fact]
    public async Task CorruptedSavedRegionIsRejectedOfflineAndCanBeReplacedOnline()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var request = new SurveyRequest(SurveyDefinition.Color.Id, new(10, 40), 2, 128);
            byte[] fits = FitsFixture(directory, request);
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fits) })));
            var service = new SurveyImageService(Path.Combine(directory, "cache"), client);
            await service.LoadAsync(request, false);
            using (var connection = new SqliteConnection($"Data Source={service.DatabasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE frames SET data=x'0001'";
                command.ExecuteNonQuery();
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(request, true));
            Assert.Empty(service.GetCached());
            Assert.False((await service.LoadAsync(request, false)).FromCache);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SurveyEvictionRetainsRecentlyUsedRegionsWithinThePayloadBudget()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var first = new SurveyRequest(SurveyDefinition.Color.Id, new(10, 40), 2, 128);
            var second = first with { FieldOfView = 3 };
            var third = first with { FieldOfView = 4 };
            var responses = new[] { first, second, third }.ToDictionary(r => r.DownloadUri,
                r => FitsFixture(directory, r));
            using var client = new HttpClient(new Handler((message, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responses[message.RequestUri!])
            })));
            long limit = responses[first.DownloadUri].LongLength * 2;
            var service = new SurveyImageService(Path.Combine(directory, "cache"), client, limit);
            await service.LoadAsync(first, false);
            await service.LoadAsync(second, false);
            await service.LoadAsync(first, true);
            await service.LoadAsync(third, false);
            var saved = service.GetCached();
            Assert.Equal(2, saved.Length);
            Assert.True(saved.Sum(s => s.Bytes) <= limit);
            Assert.Contains(saved, s => s.Request.Key == first.Key);
            Assert.Contains(saved, s => s.Request.Key == third.Key);
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.LoadAsync(second, true));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PublicSurveyEndpointSmokeTestWhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("SKYBROWSER_SURVEY_LIVE_TEST") != "1")
        {
            return;
        }
        string directory = TestImages.NewDirectory();
        try
        {
            var service = new SurveyImageService(directory);
            var frame = await service.LoadAsync(new(SurveyDefinition.Color.Id, new(10.6847, 41.269), 2, 128), false);
            Assert.Equal(128, frame.Image.Width);
            Assert.NotNull(frame.Image.Placement);
            Assert.True((await service.LoadAsync(frame.Request, true)).FromCache);
        }
        finally { Directory.Delete(directory, true); }
    }
}
