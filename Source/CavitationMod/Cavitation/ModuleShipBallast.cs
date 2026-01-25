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

        [KSPField] public float variantPumpRate;
        [KSPField] public float ECRequirement;

        [KSPField(isPersistant = true)] public bool useTargetDepth = true;


        float partBuoyancy;
        float smallestVariantMass = 0;

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

            variantPumpRate = pumpRate;
            smallestVariantMass = part.mass;

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
            if (part.checkSplashed() && pumpActive)
            {   
                // Calculate error
                float error = targetFill - fillPercent;
                float absoluteError = Math.Abs(error);

                float drainRatePerSecond = (variantPumpRate / 100) * maxBuoyancy;
                float increment = drainRatePerSecond * TimeWarp.deltaTime;

                // Check if there's enough EC
                bool hasEC = availableEC(ECRequirement);

                // Adjust buoyancy towards the target
                if (absoluteError <= 1) // Within 1% of target, no adjustment
                {
                    ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastIdle");
                }
                else if (error > 1) // Need to take in more water
                {
                    ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastFlooding");
                    partBuoyancy -= increment;
                }
                else if (error < -1) // Need to expel water
                {
                    ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastDraining");
                    partBuoyancy += increment;
                    demandEC(ECRequirement);
                }

                // Clamp buoyancy within min and max limits
                partBuoyancy = Mathf.Clamp(partBuoyancy, minBuoyancy, maxBuoyancy);
                part.buoyancy = partBuoyancy;

                // Display fill percent
                float difference = maxBuoyancy - partBuoyancy;
                fillPercent = Mathf.RoundToInt((difference / maxBuoyancy) * 100);
            }
            else
            {
                ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastIdle");
            }
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
                float drainRatePerSecond = (((variantPumpRate / 100) * maxBuoyancy) * ((float)verticalSpeedLimit / (float)10));
                float increment = drainRatePerSecond * TimeWarp.deltaTime;
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
                            partBuoyancy -= increment;
                        }
                        else if (vesselSpeed <= -verticalSpeedLimit) // descending too fast
                        {
                            partBuoyancy += increment;
                            demandEC(ECRequirement);
                        }
                        else if (error > 0) // above target depth
                        {
                            partBuoyancy -= increment;
                        }
                        else if (error < 0) // below target depth
                        {
                            partBuoyancy += increment;
                            demandEC(ECRequirement);
                        }
                    }

                    // If aiming for surface and near surface just flush the tanks entirely
                    if (partBuoyancy < maxBuoyancy && currentDepth < 10 && targetDepth == 0 && hasEC)
                    {
                        partBuoyancy += increment;
                    }

                    // Clamp buoyancy within min and max limits
                    partBuoyancy = Mathf.Clamp(partBuoyancy, minBuoyancy, maxBuoyancy);
                    part.buoyancy = partBuoyancy;

                    // Display fill percent
                    float difference =  maxBuoyancy - partBuoyancy;
                    fillPercent = Mathf.RoundToInt((difference/maxBuoyancy)*100);
                }
                else
                {
                    ballastStatus = StringUtils.Localize("#LOC_KPDynamics_BallastIdle");
                }
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
            if (appliedPart == part)
            {
                float pumpChange = smallestVariantMass / (smallestVariantMass + variant.Mass);
                variantPumpRate = pumpChange * pumpRate;
                //Debug.Log("[Cavitation] : Pump Rate" + variantPumpRate + "/s");
            }
        }
        private void OnEditorVariantApplied(Part appliedPart, PartVariant variant)
        {
            float pumpChange = smallestVariantMass / (smallestVariantMass + variant.Mass);
            variantPumpRate = pumpChange * pumpRate;
            //Debug.Log("[Cavitation] : Edit Pump Rate" + variantPumpRate + "/s");
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
