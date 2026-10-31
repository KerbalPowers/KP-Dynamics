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

namespace Cavitation
{
    //[KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ModuleShipBallast : PartModule
    {
        // This class creates a system of ballast storage and expulsion for attached parts

        const string ballastGroupName = "Ballast";
        const string ballastDisplayName = "#LOC_KPDynamics_Ballast";

        // CFG Values
        [KSPField] public float maxBuoyancy;
        [KSPField] public float minBuoyancy;
        [KSPField] public float maxSpeed;
        [KSPField] public float maxDepth;
        [KSPField] public float pumpRate;

        [KSPField] public float ECRequirement;

        [KSPField(isPersistant = true)] public bool useTargetDepth = true;

        PartResource ballastWater;
        float unitsPerSecond;
        float partBuoyancy;  // current buoyancy of the part

        #region User Settings

        [KSPEvent(guiActive = true,
            guiActiveEditor = true,
            groupName = ballastGroupName,
            groupDisplayName = ballastDisplayName,
            guiName = "#LOC_KPDynamics_ToggleControlType")]
        public void EventToggleTracking() => ToggleType();

        [KSPField(isPersistant = false,
             guiActive = true,
             guiActiveEditor = false,
             guiName = "#LOC_KPDynamics_CurrentDepth",
             guiUnits = " m",
             groupName = ballastGroupName,
             groupDisplayName = ballastDisplayName
            )]
         public int currentDepth = 0;

        [KSPField(isPersistant = false,
            guiActive = true,
            guiActiveEditor = false,
            guiName = "#LOC_KPDynamics_Flooded",
            guiUnits = "%",
            groupName = ballastGroupName,
            groupDisplayName = ballastDisplayName
           )]
        public int fillPercent = 0;

        [KSPField(isPersistant = true,
             guiActive = true,
             guiActiveEditor = true,
             guiName = "#LOC_KPDynamics_Pump",
             groupName = ballastGroupName,
             groupDisplayName = ballastDisplayName),
             UI_Toggle(
                 enabledText = "#LOC_KPDynamics_Enabled",
                 disabledText = "#LOC_KPDynamics_Disabled",
                 scene = UI_Scene.All
             )]
         public bool pumpActive = false;

         [KSPField(isPersistant = false,
             guiActive = true,
             guiActiveEditor = false,
             guiName = "#LOC_KPDynamics_Status",
             groupName = ballastGroupName,
             groupDisplayName = ballastDisplayName)]
        public string ballastStatus = "Idle";

        [KSPAxisField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_KPDynamics_TargetDepth",
            guiUnits = " m",
            groupName = ballastGroupName,
            groupDisplayName = ballastDisplayName,
            axisMode = KSPAxisMode.Incremental,
            minValue = 0f,
            maxValue = 2000f,
            incrementalSpeed = 10f),
            UI_FloatRange(
                minValue = 0f,
                maxValue = 2000f,
                stepIncrement = 25f,
                scene = UI_Scene.All
            )]
        public float targetDepth = 0f;

        [KSPAxisField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_KPDynamics_TargetFlooding",
            guiUnits = "%",
            groupName = ballastGroupName,
            groupDisplayName = ballastDisplayName,
            axisMode = KSPAxisMode.Incremental,
            minValue = 0f,
            maxValue = 100f,
            incrementalSpeed = 1f),
            UI_FloatRange(
                minValue = 0f,
                maxValue = 100f,
                stepIncrement = 1f,
                scene = UI_Scene.All
            )]
        public float targetFill = 0f;
        #endregion

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor) 
            { 
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);
                GameEvents.onEditorVariantApplied.Add(OnEditorVariantApplied);
            }

            partBuoyancy = Mathf.Clamp(part.buoyancy, minBuoyancy, maxBuoyancy);

            var floatRange = (UI_FloatRange)Fields["targetDepth"].uiControlEditor;
            floatRange.maxValue = maxDepth;

            ballastWater = part.Resources.Get("KPBallastWater");

            UpdateBuoyancyFromBallast();
            UpdatePumpData();

            UpdateUI();
        }

        #region Part Actions
        [KSPAction("#LOC_KPDynamics_ToggleControlType")]
        public void AGToggleType(KSPActionParam param) => ToggleType();

        [KSPAction("#LOC_KPDynamics_TogglePump")]
        public void AGTogglePump(KSPActionParam param) => pumpActive = !pumpActive;

        [KSPAction("#LOC_KPDynamics_EnablePump")]
        public void AGEnablePump(KSPActionParam param) => pumpActive = true;

        [KSPAction("#LOC_KPDynamics_DisablePump")]
        public void AGDisablePump(KSPActionParam param) => pumpActive = false;
        #endregion

        public override void OnUpdate()
        {
            // On physics update
            // Gradual buoyancy update in flight
            if (HighLogic.LoadedSceneIsFlight)
            {
                currentDepth = (int)Math.Round(Math.Abs(part.orbit.altitude));
                // Adjust buoyancy center to lowest corner(maybe)
                //Vector3 CenterOfBuoyancy
                // Compare with target depth and adjust flooding status
                if (useTargetDepth) { DepthPumpAdjust(); } else { FloodPumpAdjust(); }
                
            }
        }

        private void FloodPumpAdjust()
        {
            if (!part.checkSplashed() || !pumpActive)
            {
                ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastIdle");
                return;
            }

            float increment = unitsPerSecond * TimeWarp.deltaTime;
            bool hasEC = availableEC(ECRequirement);

            // Current fill in 0..1
            float fill = (float)(ballastWater.amount / ballastWater.maxAmount);
            float target = targetFill * 0.01f;   // convert % → 0..1

            float error = target - fill;

            if (Mathf.Abs(error) < 0.00001f)
            {
                ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastIdle");
                return;
            }

            if (error > 0f)   // Need to flood
            {
                ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastFlooding");

                float remaining = (float)ballastWater.maxAmount - (float)ballastWater.amount;

                // Magnetise if this frame would overshoot
                if (increment >= remaining)
                {
                    ballastWater.amount = ballastWater.maxAmount;
                }
                else
                {
                    ballastWater.amount += increment;
                }
            }
            else             // Need to drain
            {
                ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastDraining");

                float remaining = (float)ballastWater.amount;

                if (hasEC)
                {
                    if (increment >= remaining)
                    {
                        ballastWater.amount = 0f;
                    }
                    else
                    {
                        ballastWater.amount -= increment;
                    }

                    demandEC(ECRequirement);
                }
                else
                {
                    ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastEC");
                }
            }

            UpdateBuoyancyFromBallast();
        }

        private void DepthPumpAdjust()
        {
            if (part.checkSplashed())
            {
                // Calculate error
                float error = targetDepth - currentDepth;
                float absoluteError = Math.Abs(error);

                // Place a reductive speed curve to ease part to a halt starting at 100m and ending at 2m
                double verticalSpeedLimit = Mathf.Max(Mathf.Min(absoluteError / 20, maxSpeed), 0.25f);

                float increment = unitsPerSecond * TimeWarp.deltaTime;
                double vesselSpeed = vessel.verticalSpeed;

                // Check if there's enough EC
                bool hasEC = availableEC(ECRequirement);

                if (pumpActive)
                {
                    if (!hasEC)
                    {
                        ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastEC");
                    }
                    else if (error < -1)
                    {
                        ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastAscending");
                    }
                    else if (error > 1)
                    {
                        ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastDescending");
                    }
                    else
                    {
                        ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastIdle");
                    }

                    if (hasEC)
                    {
                        if (vesselSpeed >= verticalSpeedLimit) // ascending too fast
                        {
                            ballastWater.amount = Mathf.Clamp(
                                (float)ballastWater.amount + increment,
                                0f,
                                (float)ballastWater.maxAmount
                            );
                        }
                        else if (vesselSpeed <= -verticalSpeedLimit) // descending too fast
                        {
                            ballastWater.amount = Mathf.Clamp(
                                (float)ballastWater.amount - increment,
                                0f,
                                (float)ballastWater.maxAmount
                            );
                            demandEC(ECRequirement);
                        }
                        else if (error > 0) // above target depth
                        {
                            ballastWater.amount = Mathf.Clamp(
                                (float)ballastWater.amount + increment,
                                0f,
                                (float)ballastWater.maxAmount
                            );
                        }
                        else if (error < 0) // below target depth
                        {
                            ballastWater.amount = Mathf.Clamp(
                                (float)ballastWater.amount - increment,
                                0f,
                                (float)ballastWater.maxAmount
                            );
                            demandEC(ECRequirement);
                        }
                    }

                    // If aiming for surface and near surface just flush the tanks entirely
                    if (partBuoyancy < maxBuoyancy && currentDepth < 10 && targetDepth == 0 && hasEC)
                    {
                        ballastWater.amount = Mathf.Clamp(
                            (float)ballastWater.amount - increment,
                            0f,
                            (float)ballastWater.maxAmount
                        );
                    }
                }
                else
                {
                    ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastIdle");
                }

                UpdateBuoyancyFromBallast();
            }
            else
            {
                currentDepth = 0;
                ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastWaterline");
            }
            //Debug.Log("[Cavitation] Real Bouyancy: " + part.buoyancy);
        }

        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part || ballastWater == null) return;
        }
        private void OnEditorVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part || ballastWater == null) return;

            Part basePrefab = part.partInfo.partPrefab;
            float baseBallastCapacity = (float)basePrefab.Resources.Get("KPBallastWater").maxAmount;
            float baseVariantMass = basePrefab.mass;

            // Scale resource max amount
            float massDelta = Mathf.Min(baseVariantMass, (baseVariantMass + variant.Mass)) / baseVariantMass;
            float newCapacity = baseBallastCapacity * massDelta;

            Debug.Log($"[Cavitation] Setting Variant Capacity: {newCapacity}");

            // Cap current amount to new max
            ballastWater.maxAmount = newCapacity;
            ballastWater.amount = 0;

            UpdatePumpData();
        }
        private Boolean availableEC(float ECDemand)
        {
            // Make sure that at least 95% of required ec is there
            double amount;
            double maxAmount;

            part.GetConnectedResourceTotals(
                PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id,
                out amount,
                out maxAmount
            );

            return amount >= ECDemand * Time.fixedDeltaTime * 0.95;
        }

        private void demandEC(float ECDemand)
        {
            part.RequestResource(
                "ElectricCharge",
                ECRequirement * Time.fixedDeltaTime,
                ResourceFlowMode.ALL_VESSEL
            );
        }

        public void UpdateBuoyancyFromBallast()
        {
            if (ballastWater == null) return;

            float fill = (float)(ballastWater.amount / ballastWater.maxAmount);
            fill = Mathf.Clamp01(fill);

            // Convert to emptiness
            float empty = 1f - fill;

            /*
            // Compress positive buoyancy region because almost all the range is positive
            float threshold = 0.5f;          // fill fraction at which ramp accelerates
            float normalizedFill = Mathf.Min(fill / threshold, 1f);

            float curveFactor = 3f; // higher = more pronounced ramp near full
            float buoyancyFrac = Mathf.Pow(fill, curveFactor); // simple exponent curve
            */

            // full tank → min buoyancy
            partBuoyancy = Mathf.Lerp(maxBuoyancy, minBuoyancy, fill);
            part.buoyancy = partBuoyancy;

            //Debug.Log($"[Cavitation] Fill: {fill}, Buoyancy: {partBuoyancy}");

            fillPercent = Mathf.RoundToInt(fill * 100f);
        }

        private void UpdatePumpData()
        {
            // Convert pumpRate (% of base capacity per second) to units per second using base variant capacity
            float baseCapacity = (float)part.partInfo.partPrefab.Resources.Get("KPBallastWater").maxAmount;
            unitsPerSecond = (pumpRate / 100f) * baseCapacity;
        }

        public override string GetInfo()
        {
            string returnString =   "Descent/Ascent Velocity: " + maxSpeed + "m/s" +
                                    "\nMaximum Depth: " + maxDepth + "m" +
                                    "\nBuoyancy Range: " + minBuoyancy + " - " + maxBuoyancy +
                                    "\nPump Rate: " + pumpRate + "%/s (Base)" +
                                    "\n\nRequires " + ECRequirement + "ec/s when pumping";
            return returnString;
        }
        public void ToggleType()
        {
            useTargetDepth = !useTargetDepth;
            UpdateUI();
        }
        private void UpdateUI()
        {
            Fields["targetDepth"].guiActive = Fields["targetDepth"].guiActiveEditor = useTargetDepth;
            Fields["targetFill"].guiActive = Fields["targetFill"].guiActiveEditor = !useTargetDepth;
        }

        private void OnUseTargetDepthChanged(BaseField field, object obj)
        {
            UpdateUI();
        }
    }
}
