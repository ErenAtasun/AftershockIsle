using AftershockIsle.Building;
using UnityEditor;
using UnityEngine;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Blender'da "kapi_acik_aci" özelliği taşıyan objelere (Building.fbx kapıları) import sırasında BuildingDoor ekler.
    /// Blender sağ elli, Unity sol elli olduğu için menteşe açısının işareti ters çevrilir.
    /// </summary>
    public class BuildingDoorImporter : AssetPostprocessor
    {
        const string AngleProperty = "kapi_acik_aci";

        // Sürüm artınca Unity modelleri bu işlemciyle yeniden import eder.
        public override uint GetVersion() => 1;

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            int index = System.Array.IndexOf(names, AngleProperty);
            if (index < 0) return;

            var door = go.GetComponent<BuildingDoor>();
            if (door == null) door = go.AddComponent<BuildingDoor>();
            door.Configure(-System.Convert.ToSingle(values[index]));
        }
    }
}
