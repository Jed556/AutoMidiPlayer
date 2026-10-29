fn main() {
  println!("cargo:rerun-if-env-changed=AMP_UPDATER_PUBLIC_KEY");

  if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows")
    && std::env::var("CARGO_CFG_TARGET_ENV").as_deref() == Ok("msvc")
  {
    // Imports are resolved before main(), so runtime DLL search settings alone
    // cannot protect them from unrelated DLLs beside a portable executable.
    // LOAD_LIBRARY_SEARCH_SYSTEM32 applies to our Windows system imports.
    println!("cargo:rustc-link-arg=/DEPENDENTLOADFLAG:0x800");
  }

  println!("cargo:rerun-if-changed=windows-app-manifest.xml");
  let windows =
    tauri_build::WindowsAttributes::new().app_manifest(include_str!("windows-app-manifest.xml"));
  tauri_build::try_build(tauri_build::Attributes::new().windows_attributes(windows))
    .expect("failed to run Tauri build script");
}
