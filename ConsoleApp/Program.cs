using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

var httpClient = new HttpClient();

// 1. Call /api/time anonymously
Console.WriteLine("Fetching current time from WebApi...");
var timeResponse = await httpClient.GetFromJsonAsync<TimeResponse>("http://localhost:5001/api/time");
Console.WriteLine($"Current time: {timeResponse?.Time}");

// 2. Get access token via client credentials
Console.WriteLine("\nObtaining access token from Oidc.Idp...");
var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["grant_type"] = "client_credentials",
    ["client_id"] = "m2m-client",
    ["client_secret"] = "m2m-secret",
    ["scope"] = "api"
});

var tokenHttpResponse = await httpClient.PostAsync("http://localhost:5000/connect/token", tokenRequest);
tokenHttpResponse.EnsureSuccessStatusCode();

var tokenJson = await tokenHttpResponse.Content.ReadAsStringAsync();
var tokenDoc = JsonDocument.Parse(tokenJson);
var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
Console.WriteLine("Access token obtained.");

// 3. Call /api/userinfo with access token
Console.WriteLine("\nFetching user info from WebApi...");
var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5001/api/userinfo");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

var userInfoResponse = await httpClient.SendAsync(request);
userInfoResponse.EnsureSuccessStatusCode();

var userInfoJson = await userInfoResponse.Content.ReadAsStringAsync();
Console.WriteLine($"User info: {userInfoJson}");

record TimeResponse([property: JsonPropertyName("time")] string Time);

