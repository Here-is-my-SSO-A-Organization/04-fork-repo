using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Web;
using Microsoft.Identity.Abstractions;
using Microsoft.Data.SqlClient;

namespace sign_in_webapp.Pages;

[AuthorizeForScopes(ScopeKeySection = "DownstreamApis:MicrosoftGraph:Scopes")]
public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IDownstreamApi _downstreamWebApi;
    private readonly IConfiguration _configuration;

    public IndexModel(ILogger<IndexModel> logger,
                        IDownstreamApi downstreamWebApi,
                        IConfiguration configuration)
    {
        _logger = logger;
        _downstreamWebApi = downstreamWebApi;
        _configuration = configuration;
    }

    public async Task OnGet()
    {
        // Microsoft Graph API call
        using var response = await _downstreamWebApi.CallApiForUserAsync("MicrosoftGraph").ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var apiResult = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
            ViewData["ApiResult"] = JsonSerializer.Serialize(apiResult, new JsonSerializerOptions { WriteIndented = true });
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new HttpRequestException($"Invalid status code in the HttpResponseMessage: {response.StatusCode}: {error}");
        }

        // SQL Managed Instance connection test
        await TestSqlMIConnectionAsync();
    }

    private async Task TestSqlMIConnectionAsync()
    {
        var connectionString = _configuration.GetConnectionString("SqlMI");
        if (string.IsNullOrEmpty(connectionString))
        {
            ViewData["SqlMIStatus"] = "Failed";
            ViewData["SqlMIMessage"] = "Connection string 'SqlMI' is not configured in appsettings.json.";
            return;
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand("SELECT @@VERSION AS Version, GETDATE() AS ServerTime, DB_NAME() AS DatabaseName", connection);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                sw.Stop();
                ViewData["SqlMIStatus"] = "Success";
                ViewData["SqlMIVersion"] = reader["Version"]?.ToString();
                ViewData["SqlMIServerTime"] = reader["ServerTime"]?.ToString();
                ViewData["SqlMIDatabase"] = reader["DatabaseName"]?.ToString();
                ViewData["SqlMILatency"] = $"{sw.ElapsedMilliseconds} ms";
            }

            _logger.LogInformation("SQL MI connection test succeeded in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL MI connection test failed");
            ViewData["SqlMIStatus"] = "Failed";
            ViewData["SqlMIMessage"] = ex.Message;
        }
    }
}
