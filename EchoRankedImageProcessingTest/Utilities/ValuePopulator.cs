using System.Reflection;

namespace EchoRankedImageProcessingTest.Utilities;

public static class ValuePopulator
{
    private static readonly Random Random = new();
    private static int GetRandomValueForInt() => Random.Next(0, 100);
    private static bool GetRandomValueForBool() => Random.Next(0, 2) == 1;
    private static float GetRandomValueForFloat() => (float)Random.NextDouble() * 100f;

    extension(object target)
    {
        public void SetApplicableValuesWithinObject()
        {
            target.SetIntegersWithinObject();
            target.SetBooleansWithinObject();
            target.SetFloatsWithinObject();
        }

        public void SetIntegersWithinObject()
        {
            var type = target.GetType();

            foreach (var property in type.GetProperties())
            {
                if (!property.CanWrite || !property.CanRead) continue;

                // Fixed Type.GetType using type-safe typeof()
                if (property.PropertyType != typeof(int) && property.PropertyType != typeof(int?))
                    continue;
                if (!property.HasValue(target))
                {
                    property.SetValue(target, GetRandomValueForInt());
                }
            }
        }

        private void SetBooleansWithinObject()
        {
            var type = target.GetType();

            foreach (var property in type.GetProperties())
            {
                if (!property.CanWrite || !property.CanRead)
                    continue;

                // Fixed conditional operator priority logic error
                if (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?))
                    continue;
                if (!property.HasValue(target))
                {
                    property.SetValue(target, GetRandomValueForBool());
                }
            }
        }

        private void SetFloatsWithinObject()
        {
            var type = target.GetType();

            foreach (var property in type.GetProperties())
            {
                if (!property.CanWrite || !property.CanRead)
                    continue;

                if (property.PropertyType != typeof(float) && property.PropertyType != typeof(float?))
                    continue;
                if (!property.HasValue(target))
                {
                    property.SetValue(target, GetRandomValueForFloat());
                }
            }
        }
    }

    private static bool HasValue(this PropertyInfo property, object target)
    {
        var value = property.GetValue(target);
        if (value == null)
            return false;

        if (property.PropertyType == typeof(float) && (float)value == 0.0f)
            return false;
        if (property.PropertyType == typeof(int) && (int)value == 0)
            return false;
        return property.PropertyType != typeof(bool) || (bool)value;
    }
}