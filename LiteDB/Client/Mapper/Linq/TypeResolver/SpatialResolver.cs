using System;
using System.Reflection;
using LiteDB.Spatial;

namespace LiteDB
{
    internal class SpatialResolver : ITypeResolver
    {
        public string ResolveMethod(MethodInfo method)
        {
            switch (method.Name)
            {
                case nameof(SpatialExpressions.Near):
                    return ResolveNearPattern(method);
                case nameof(SpatialExpressions.Within):
                    return "SPATIAL_WITHIN(@0, @1)";
                case nameof(SpatialExpressions.Intersects):
                    return "SPATIAL_INTERSECTS(@0, @1)";
                case nameof(SpatialExpressions.Contains):
                    return "SPATIAL_CONTAINS(@0, @1)";
                case nameof(SpatialExpressions.WithinBoundingBox):
                    return "SPATIAL_WITHIN_BOX(@0, @1, @2, @3, @4)";
            }

            return null;
        }

        public string ResolveMember(MemberInfo member) => null;

        public string ResolveCtor(ConstructorInfo ctor) => null;

        private string ResolveNearPattern(MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (parameters.Length == 3)
            {
                var formula = Spatial.Spatial.Options.Distance.ToString();
                return $"SPATIAL_NEAR(@0, @1, @2, '{formula}')";
            }

            if (parameters.Length == 4)
            {
                return "SPATIAL_NEAR(@0, @1, @2, @3)";
            }

            throw new NotSupportedException("Unsupported overload for SpatialExpressions.Near");
        }
    }
}
