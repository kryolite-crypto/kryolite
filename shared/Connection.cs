namespace Kryolite.Shared;

public static class Connection
{
    public static bool TestConnection(Uri uri)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);

            var pingUri = new UriBuilder(uri)
            {
                Path = "whatisthis"
            };

            var request = new HttpRequestMessage(HttpMethod.Get, pingUri.Uri);
            var response = httpClient.Send(request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<bool> TestConnectionAsync(Uri uri)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);

            var pingUri = new UriBuilder(uri)
            {
                Path = "whatisthis"
            };

            var request = new HttpRequestMessage(HttpMethod.Get, pingUri.Uri);
            var response = await httpClient.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
