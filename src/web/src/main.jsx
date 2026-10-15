import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import { UserProvider } from "./user";
import { DialogProvider } from "./Dialogs";
import "./styles.css";

ReactDOM.createRoot(document.getElementById("root")).render(
    <React.StrictMode>
        <BrowserRouter>
            <UserProvider>
                <DialogProvider>
                    <App />
                </DialogProvider>
            </UserProvider>
        </BrowserRouter>
    </React.StrictMode>
);