//! Includes the application icon in the Windows executable resource table.

/// Adds a multi-resolution icon to Windows builds for Explorer and shortcuts.
#[cfg(windows)]
fn main() {
    winresource::WindowsResource::new()
        .set_icon("assets/guanacaste-render-icon.ico")
        .compile()
        .expect("Failed to embed the Windows application icon");
}

/// Keeps non-Windows builds independent of Windows resource tooling.
#[cfg(not(windows))]
fn main() {}
