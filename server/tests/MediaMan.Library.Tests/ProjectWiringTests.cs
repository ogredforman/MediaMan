using System.Reflection;

namespace MediaMan.Library.Tests;

// Placeholder from PLAN 0.1: proves the test project builds, runs and references its module.
// Replace with real tests as the module gains code.
public class ProjectWiringTests
{
    [Fact]
    public void Module_assembly_is_referenced()
    {
        var assembly = Assembly.Load(new AssemblyName("MediaMan.Library"));

        assembly.GetName().Name.Should().Be("MediaMan.Library");
    }
}
