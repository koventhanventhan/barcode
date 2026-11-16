using System.Reflection;

namespace EduFlow.Infrastructure;

public static class InfrastructureAssembly
{
    public static Assembly Assembly => typeof(InfrastructureAssembly).Assembly;
}
