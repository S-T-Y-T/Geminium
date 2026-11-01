using System.Linq;
using NUnit.Framework;
using Geminium.Core;

namespace Geminium.Tests.EditMode
{
    // Сторожевые тесты архитектуры: ядро должно оставаться чистым C#,
    // иначе его нельзя будет запускать без графики.
    public class CoreArchitectureTests
    {
        [Test]
        public void Core_DoesNotReferenceUnityEngineOrEditor()
        {
            var unityReferences = typeof(CoreInfo).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name.StartsWith("UnityEngine") || name.StartsWith("UnityEditor"))
                .ToArray();

            Assert.IsEmpty(unityReferences, "Ядро ссылается на движок: " + string.Join(", ", unityReferences));
        }
    }
}
