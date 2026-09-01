import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import { themeManager } from "./lib/theme";
import "./styles.css";

// Apply the stored theme before first paint (no wrong-theme flash), then
// follow Windows theme/accent changes live.
themeManager.init();

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);