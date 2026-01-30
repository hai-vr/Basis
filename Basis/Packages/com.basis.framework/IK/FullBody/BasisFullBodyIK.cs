namespace UnityEngine.Animations.Rigging
{
    [DisallowMultipleComponent]
    public class BasisFullBodyIK : RigConstraint<BasisFullIKConstraintJob, BasisFullBodyData, BasisFullBodyJobBinder>
    {
        protected override void OnValidate()
        {
            base.OnValidate();
            // force serialize dirty for animated bools
            m_Data.hintWeightHead = m_Data.hintWeightHead;
            m_Data.HintWeightLeftLowerLeg = m_Data.HintWeightLeftLowerLeg;
            m_Data.HintWeightRightLowerLeg = m_Data.HintWeightRightLowerLeg;
            m_Data.EnabledSpineIK = m_Data.EnabledSpineIK;

            // new toggles
            m_Data.LeftToeEnabled = m_Data.LeftToeEnabled;
            m_Data.RightToeEnabled = m_Data.RightToeEnabled;

            // hands toggles
            m_Data.hintWeightLeftHand = m_Data.hintWeightLeftHand;
            m_Data.hintWeightRightHand = m_Data.hintWeightRightHand;
            m_Data.enabledLeftHand = m_Data.enabledLeftHand;
            m_Data.enabledRightHand = m_Data.enabledRightHand;
            m_Data.protectElbow = m_Data.protectElbow;
        }
    }
}
