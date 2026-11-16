using System.Reflection;

namespace EduFlow.Application;

public static class ApplicationAssembly
{
    public static Assembly Assembly => typeof(ApplicationAssembly).Assembly;
}
