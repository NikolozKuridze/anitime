using Anitime.Application.Validation;
using Anitime.Contracts;
using Anitime.Domain.Enums;
using FluentValidation.TestHelper;
using Xunit;

namespace Anitime.Application.Tests;

public sealed class UserSettingsValidatorTests
{
    private readonly UpdateUserSettingsRequestValidator _validator = new();

    [Theory]
    [InlineData(BackgroundStyleType.Solid)]
    [InlineData(BackgroundStyleType.Gradient)]
    public void Settings_WithoutColours_AreAccepted(BackgroundStyleType style)
    {
        var model = new UpdateUserSettingsRequest(style, null, "", null, null, null);

        _validator.TestValidate(model).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Gradient_WithoutColours_FallsBackToStoredThenDefault()
    {
        Assert.Equal(("#111", "#222"), UserSettingsRules.ResolveColors(BackgroundStyleType.Gradient, null, " ", "#111", "#222"));
        Assert.Equal((UserSettingsRules.DefaultPrimaryColor, UserSettingsRules.DefaultSecondaryColor),
            UserSettingsRules.ResolveColors(BackgroundStyleType.Gradient, null, null, null, null));
        Assert.Equal(("#abc", "#abc"), UserSettingsRules.ResolveColors(BackgroundStyleType.Gradient, "#abc", null, null, null));
    }

    [Fact]
    public void Transparent_KeepsWhatWasSent()
    {
        Assert.Equal((null, null), UserSettingsRules.ResolveColors(BackgroundStyleType.Transparent, "", null, "#111", "#222"));
    }
}
