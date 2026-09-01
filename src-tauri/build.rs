fn main() {
    tauri_build::build();

    // Make the statically-linked libunwind (committed in src-tauri/static/)
    // visible to the linker on Windows builds, so the exe has no libunwind.dll
    // dependency. Portable: resolved from the manifest dir, no absolute paths.
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        let static_dir = std::path::PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap())
            .join("static");
        println!("cargo:rustc-link-search=native={}", static_dir.display());
    }
}
