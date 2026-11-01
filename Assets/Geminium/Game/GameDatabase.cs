using UnityEngine;
using Geminium.Core.Data;

namespace Geminium.Game
{
    // Файл данных игры: Assets/Geminium/Data/GameDatabase.asset.
    // Числа меняются в инспекторе; при ошибке в данных в Console появляется предупреждение.
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "Geminium/Game Database")]
    public sealed class GameDatabase : ScriptableObject
    {
        public GameData Data = new GameData();

        public DataCatalog CreateCatalog() => new DataCatalog(Data);

        private void OnValidate()
        {
            foreach (string error in GameDataValidator.Validate(Data))
                Debug.LogWarning("GameDatabase: " + error, this);
        }
    }
}
