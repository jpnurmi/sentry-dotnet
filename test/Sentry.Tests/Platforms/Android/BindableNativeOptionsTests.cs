#if ANDROID
using Microsoft.Extensions.Configuration;

namespace Sentry.Tests.Platforms.Android;

public class BindableNativeOptionsTests : BindableTests<SentryOptions.NativeOptions>
{
    public BindableNativeOptionsTests()
        : base(nameof(BindableSentryOptions.NativeOptions.ExperimentalOptions))
    {
    }

    [Fact]
    public void BindableProperties_MatchOptionsProperties()
    {
        var actual = GetPropertyNames<BindableSentryOptions.NativeOptions>();
        AssertContainsAllOptionsProperties(actual);
    }

    [Fact]
    public void ApplyTo_SetsOptionsFromConfig()
    {
        // Arrange
        var actual = new SentryOptions.NativeOptions(new SentryOptions());
        var bindable = new BindableSentryOptions.NativeOptions();

        // Act
        Fixture.Config.Bind(bindable);
        bindable.ApplyTo(actual);

        // Assert
        AssertContainsExpectedPropertyValues(actual);
    }
}
#endif
