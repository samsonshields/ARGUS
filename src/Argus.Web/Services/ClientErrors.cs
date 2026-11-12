namespace Argus.Web.Services;

public static class ClientErrors
{
    public static string Describe(HttpRequestException error) => error.StatusCode.HasValue ? error.Message :
        "Cannot reach the API. Check that it is running, then reload this view.";
}
