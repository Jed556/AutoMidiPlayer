/// Passive observations shared by native services. Reading a status never
/// creates an output device or requests operating-system permissions.
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub enum ServiceState {
    Ready,
    Inactive,
    #[default]
    NotChecked,
    Checking,
    PermissionRequired,
    Reconnecting,
    Unavailable,
    Unknown,
}

#[derive(Debug, Default, Clone, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ServiceStatus {
    pub state: ServiceState,
    pub detail: Option<String>,
}

impl ServiceStatus {
    pub fn new(state: ServiceState) -> Self {
        Self {
            state,
            detail: None,
        }
    }
    pub fn with_detail(state: ServiceState, detail: impl ToString) -> Self {
        Self {
            state,
            detail: Some(detail.to_string()),
        }
    }
}
