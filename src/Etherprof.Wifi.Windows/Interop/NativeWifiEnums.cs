namespace Etherprof.Wifi.Windows.Interop;

public enum WLAN_INTERFACE_STATE
{
    wlan_interface_state_not_ready,
    wlan_interface_state_connected,
    wlan_interface_state_ad_hoc_network_formed,
    wlan_interface_state_disconnecting,
    wlan_interface_state_disconnected,
    wlan_interface_state_associating,
    wlan_interface_state_discovering,
    wlan_interface_state_authenticating
}

public enum WLAN_OPCODE_VALUE_TYPE
{
    wlan_opcode_value_type_query_only,
    wlan_opcode_value_type_set_by_group_policy,
    wlan_opcode_value_type_legacy_user,
    wlan_opcode_value_type_user
}

public enum WLAN_INTF_OPCODE
{
    wlan_intf_opcode_autoconf_enabled = 1,
    wlan_intf_opcode_background_scan_enabled,
    wlan_intf_opcode_media_streaming_mode,
    wlan_intf_opcode_radio_state,
    wlan_intf_opcode_bss_type,
    wlan_intf_opcode_interface_state,
    wlan_intf_opcode_current_connection,
    wlan_intf_opcode_channel_number,
    wlan_intf_opcode_supported_infrastructure_auth_cipher_pairs,
    wlan_intf_opcode_supported_adhoc_auth_cipher_pairs,
    wlan_intf_opcode_supported_country_or_region_string_list,
    wlan_intf_opcode_current_operation_mode,
    wlan_intf_opcode_supported_safe_mode,
    wlan_intf_opcode_certified_safe_mode,
    wlan_intf_opcode_hosted_network_capable,
    wlan_intf_opcode_management_frame_protection_capable,
    wlan_intf_opcode_secondary_sta_interfaces,
    wlan_intf_opcode_secondary_sta_synchronized_connections,
    wlan_intf_opcode_realtime_connection_quality = 104, // 0x68 Windows 11 opcode
    wlan_intf_opcode_rssi = 0x10000001
}

public enum WLAN_NOTIFICATION_SOURCE : uint
{
    WLAN_NOTIFICATION_SOURCE_NONE = 0,
    WLAN_NOTIFICATION_SOURCE_ACM = 0x00000008,
    WLAN_NOTIFICATION_SOURCE_MSM = 0x00000010,
    WLAN_NOTIFICATION_SOURCE_ALL = 0x0000FFFF
}

public enum WLAN_NOTIFICATION_ACM
{
    wlan_notification_acm_start = 0,
    wlan_notification_acm_autoconf_enabled,
    wlan_notification_acm_autoconf_disabled,
    wlan_notification_acm_background_scan_enabled,
    wlan_notification_acm_background_scan_disabled,
    wlan_notification_acm_bss_type_change,
    wlan_notification_acm_power_setting_change,
    wlan_notification_acm_scan_complete,
    wlan_notification_acm_scan_fail,
    wlan_notification_acm_connection_start,
    wlan_notification_acm_connection_complete,
    wlan_notification_acm_connection_attempt_fail,
    wlan_notification_acm_disconnecting,
    wlan_notification_acm_disconnected,
    wlan_notification_acm_adhoc_network_state_change,
    wlan_notification_acm_end
}

public enum WLAN_NOTIFICATION_MSM
{
    wlan_notification_msm_start = 0,
    wlan_notification_msm_associating,
    wlan_notification_msm_associated,
    wlan_notification_msm_authenticating,
    wlan_notification_msm_connected,
    wlan_notification_msm_roaming_start,
    wlan_notification_msm_roaming_end,
    wlan_notification_msm_radio_state_change,
    wlan_notification_msm_signal_quality_change,
    wlan_notification_msm_disconnecting,
    wlan_notification_msm_disconnected,
    wlan_notification_msm_peer_connect,
    wlan_notification_msm_peer_disconnect,
    wlan_notification_msm_end
}

public enum DOT11_PHY_TYPE : uint
{
    dot11_phy_type_unknown = 0,
    dot11_phy_type_any = 0,
    dot11_phy_type_fhss = 1,
    dot11_phy_type_dsss = 2,
    dot11_phy_type_irbaseband = 3,
    dot11_phy_type_ofdm = 4,
    dot11_phy_type_hrdsss = 5,
    dot11_phy_type_erp = 6,
    dot11_phy_type_ht = 7,     // 802.11n
    dot11_phy_type_vht = 8,    // 802.11ac
    dot11_phy_type_dmg = 9,    // 802.11ad
    dot11_phy_type_he = 10,    // 802.11ax (Wi-Fi 6)
    dot11_phy_type_eht = 11    // 802.11be (Wi-Fi 7)
}
