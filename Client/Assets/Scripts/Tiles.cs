using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The three kinds of ground at minimap scale, where a cell is two pixels and
    /// an edge band would be a smear. Flat fills only, in the same order of value
    /// as the field, so the shape of the map is the same shape twice.
    ///
    /// This used to also bake the whole map to one texture for a flat ground
    /// quad. The field is a mesh now (FieldMesh), lit and shaped from the
    /// simulation's heights, and the bake went with the quad; what the minimap
    /// needs survived, because the minimap is still a picture.
    /// </summary>
    public static class Tiles
    {
        public static Color32 MinimapTint(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Water: return UiStyle.MinimapWater;
                case TileKind.Rock: return UiStyle.MinimapRock;
                default: return UiStyle.MinimapGround;
            }
        }
    }
}
