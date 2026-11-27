using UnityEngine;

namespace PLC_AIPanelUI_Handoff_20261127
{

// Lives on the panel root, so button clicks and external SetActive calls stay consistent.
public sealed class AIPanelLauncherVisibility : MonoBehaviour
{
    [SerializeField] private GameObject launcher;

    private void OnEnable() { if (launcher != null) launcher.SetActive(false); }
    private void OnDisable() { if (launcher != null) launcher.SetActive(true); }
}

}
