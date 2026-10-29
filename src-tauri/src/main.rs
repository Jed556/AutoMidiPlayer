// Prevents additional console window on Windows in release, DO NOT REMOVE!!
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[cfg(windows)]
mod windows_dll_search;

fn main() {
  #[cfg(windows)]
  windows_dll_search::restrict_to_system_directory()
    .expect("Failed to configure the Windows DLL search path");

  app_lib::run();
}
