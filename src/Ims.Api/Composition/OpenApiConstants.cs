using Microsoft.AspNetCore.Authentication.JwtBearer;
namespace Ims.Api;

public static class OpenApiConstants
{
    public const string DocumentName = "v1";
    public const string DocumentPath = "/openapi/v1.json";
    public const string UiRoute = "swagger";
    public static readonly string Title = AppIdentity.ApiTitle;
    public const string BearerScheme = JwtBearerDefaults.AuthenticationScheme;
    public const string BearerFormat = "JWT";
    public const string BearerDescription = "Paste the access token from POST /api/auth/login (without the word Bearer).";
}
