using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using KSP.IO;
using KSP.UI.Screens;
using Caviation;
using BDArmory.Damage;

namespace Cavitation
{
    //[KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ModuleBallastDamage : PartModule
    {

        // CFG Values
        [KSPField] public float damageThreshold; 
        [KSPField] public float floodRate;

        HitpointTracker hitpointModule;
        PartResource ballastWater;

        float unitsPerSecond;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);
                GameEvents.onEditorVariantApplied.Add(OnEditorVariantApplied);
            }

            hitpointModule = part.Modules.GetModule<HitpointTracker>();
            ballastWater = part.Resources.Get("KPBallastWater");
        }

        public override void OnUpdate()
        {
            // On physics update
            if (HighLogic.LoadedSceneIsFlight)
            {
                FloodFromDamage();
            }
        }

        private void FloodFromDamage()
        {
            if (!part.checkSplashed()) return;
            if (hitpointModule == null || ballastWater == null) return;

            float hp = hitpointModule.Hitpoints;
            float maxHp = hitpointModule.GetMaxHitpoints();

            // 0-1 structural damage
            float damageFrac = 1f - (hp / maxHp);

            // No flooding before threshold
            if (damageFrac <= damageThreshold) return;

            // Normalize breach severity (0-1 above threshold)
            float breach = (damageFrac - damageThreshold) / (1f - damageThreshold);

            float maxWater = (float)ballastWater.maxAmount;
            float water = (float)ballastWater.amount;

            // Flooding speed: scaled by breach and empty volume
            float floodRate = breach * maxWater * damageThreshold;

            float delta = floodRate * TimeWarp.deltaTime;

            ballastWater.amount = Mathf.Clamp(
                water + delta,
                0f,
                maxWater
            );
        }
        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part || ballastWater == null) return;
        }
        private void OnEditorVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part || ballastWater == null) return;

            UpdateFloodingData(variant);
        }

        private void UpdateFloodingData(PartVariant variant)
        {
            // Convert floodRate (% of base capacity per second) to units per second in a given variant
            Part basePrefab = part.partInfo.partPrefab;
            float baseBallastCapacity = (float)basePrefab.Resources.Get("KPBallastWater").maxAmount;
            float baseVariantMass = basePrefab.mass;

            // Scale resource max amount
            float massDelta = Mathf.Min(baseVariantMass, (baseVariantMass + variant.Mass)) / baseVariantMass;

            unitsPerSecond = (floodRate / 100f) * massDelta;

            Debug.Log($"[Cavitation] Setting Variant FloodingRate: {unitsPerSecond}");
        }

    }
}
