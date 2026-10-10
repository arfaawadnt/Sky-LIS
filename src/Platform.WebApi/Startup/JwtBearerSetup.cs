using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Platform.Infrastructure.Security;
using Platform.Infrastructure.Security.Tokens;
using Platform.WebApi.Security;

namespace Platform.WebApi.Startup;

/// <summary>
/// Bearer validation of 02 §4.3: ES256 only, <c>typ at+jwt</c>, issuer and audience pinned, 60 s skew, inbound
/// claims not mapped, no role claim type. <see cref="EncryptedSubjectEvents"/> then decrypts <c>sub</c> and loads
/// the session. Every endpoint requires an authenticated user unless it carries <c>[AllowAnonymous]</c>.
/// </summary>
public static class JwtBearerSetup
{
    public static readonly string[] ValidTokenTypes = [AccessTokenService.TokenType];
    public static readonly string[] ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256];

    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<SigningKeys, SecurityOptions>((bearer, keys, options) =>
        {
            bearer.MapInboundClaims = false;
            bearer.SaveToken = false;
            bearer.IncludeErrorDetails = false;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                IssuerSigningKey = keys.ValidationKey,
                ValidAlgorithms = ValidAlgorithms,
                ValidTypes = ValidTokenTypes,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(60),
                NameClaimType = "sub",
                RoleClaimType = "platform:no-role-claim",
            };
            bearer.Events = EncryptedSubjectEvents.Create();
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
