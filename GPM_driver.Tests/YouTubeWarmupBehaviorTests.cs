using System.Linq;
using GPM_driver.Models;
using GPM_driver.Services.YouTube.Behaviors;
using Xunit;

namespace GPM_driver.Tests;

public class YouTubeWarmupBehaviorTests
{
    [Theory]
    [InlineData("search")]
    [InlineData("Searches")]
    public void SearchBehaviorMatchesAliases(string alias)
    {
        var behavior = new SearchWarmupBehavior();

        Assert.True(behavior.Matches(alias));
    }

    [Fact]
    public void Calculator_PrioritizesSearchOnFreshLanding()
    {
        var calculator = new BehaviorWeightCalculator();
        var config = new YouTubeWarmupSettings
        {
            Behaviors = new[] { "home", "search" }
        };

        var behaviors = new IYouTubeWarmupBehavior[]
        {
            new HomeWarmupBehavior(),
            new SearchWarmupBehavior()
        };

        var weighted = calculator.Calculate(behaviors, config, freshLanding: true);

        Assert.Contains(weighted, entry => entry.Behavior is SearchWarmupBehavior && entry.Weight > 0);
        Assert.DoesNotContain(weighted, entry => entry.Behavior is HomeWarmupBehavior && entry.Weight > 0);

        var chosen = calculator.PickBehavior(weighted, new System.Random(0));
        Assert.IsType<SearchWarmupBehavior>(chosen);
    }
}
