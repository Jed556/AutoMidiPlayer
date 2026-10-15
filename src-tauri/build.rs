fn main() {
  println!("cargo:rerun-if-env-changed=AMP_UPDATER_PUBLIC_KEY");
  tauri_build::build()
}
