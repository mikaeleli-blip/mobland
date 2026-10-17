using UnityEngine;

namespace Mobland.City
{
    /// <summary>Links a building's GameObject back to its data. Click targets use this.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingView : MonoBehaviour
    {
        public BuildingInfo Info;
    }
}
