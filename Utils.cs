using Comfort.Common;
using EFT;
using UnityEngine;

namespace flir.enemymarkers
{
    public abstract class Utils
    {
        internal static Vector2 ScreenResolution()
        {
            return new Vector2(Screen.width, Screen.height);
        }

        internal static bool IsInViewport(Vector3 viewportPosition)
        {
            return viewportPosition.z > 0 && viewportPosition.x >= 0 && viewportPosition.x <= 1 && viewportPosition.y >= 0 && viewportPosition.y <= 1;
        }
        
        internal static Color MarkerColor(IPlayer p)
        {
            var color = Color.clear;

            if (p.IsGroupedWithMainPlayer())
            {
                color = Plugin.PlayerGroupColor.Value;
            }
            else if (p.IsTrackedBoss())
            {
                color = Plugin.BossColor.Value;
            }
            else if (p.IsTrackedGuard())
            {
                color = Plugin.GuardColor.Value;
            }
            else if (p.IsScav())
            {
                color = Plugin.ScavColor.Value;
            }
            else if (p.IsPMC())
            {
                color = p.Side == EPlayerSide.Bear
                    ? Plugin.PmcBearColor.Value
                    : Plugin.PmcUsecColor.Value;
            }
            return color;
        }

        /**
        * Return true if the end position is within line of sight of the player
        */
        internal static bool IsLineOfSight(Vector3 startPos, Vector3 endPos, int layerMask)
        {
            // LineCast returns true if it hits a HighPolyCollider (and optionally Foliage),
            // indicating the item isn't within line of sight of the player's head
            return !Physics.Linecast(startPos, endPos, layerMask);
        }
    }
}
