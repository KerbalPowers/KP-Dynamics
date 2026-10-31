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
    public class ModuleSplashedThrust : ModuleEnginesFX
    {
        // This class extends the stock engine module to provide in-water only engines

        [KSPField(isPersistant = true)]
        public bool initialFlameout;

        [KSPField(isPersistant = true)]
        public float trueMaxThrust;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            trueMaxThrust = base.maxThrust;
        }

        public override void OnCenterOfThrustQuery(CenterOfThrustQuery qry)
        {
            if (thrustTransforms == null || thrustTransforms.Count == 0)
                return;

            // Average position and direction across all thrust transforms
            Vector3 avgPos = Vector3.zero;
            Vector3 avgDir = Vector3.zero;

            foreach (Transform t in thrustTransforms)
            {
                avgPos += t.position;
                avgDir += t.forward;
            }

            avgPos /= thrustTransforms.Count;
            avgDir /= thrustTransforms.Count;

            qry.pos = avgPos;
            qry.dir = avgDir.normalized;
            qry.thrust = trueMaxThrust;
        }

        public override void FXUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                base.FXUpdate();
                return;
            }

            if (base.CheckTransformsUnderwater())
            {
                base.status = StringUtils.Localize("#LOC_KPDynamics_ThrustNominal");
                base.finalThrust = (base.currentThrottle * trueMaxThrust) * (float)vessel.mainBody.oceanDensity;
                base.multIsp = 1f;
            }
            else
            {
                base.status = StringUtils.Localize("#LOC_KPDynamics_ThrustWaterline");
                base.finalThrust = 0;//(base.currentThrottle * trueMaxThrust) * (float)(vessel.mainBody.atmDensityASL / 830f);
                base.multIsp = 0; //0.01f;
                //TODO: Toggle bubble fx directly so can have limited thrust
            }

            base.FXUpdate();
        }

    }
}
