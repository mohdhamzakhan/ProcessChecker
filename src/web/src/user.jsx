import { createContext, useContext, useEffect, useState } from "react";
import { api, getToken, setToken } from "./api";

const Ctx = createContext(null);

export function UserProvider({ children }) {
    const [me, setMe] = useState(null);
    const [loading, setLoading] = useState(!!getToken());

    useEffect(() => {
        if (!getToken()) return;
        api.get("/api/auth/me").then(setMe).catch(() => setToken(null)).finally(() => setLoading(false));
    }, []);

    useEffect(() => {
        const h = () => setMe(null);
        window.addEventListener("pc-unauthorized", h);
        return () => window.removeEventListener("pc-unauthorized", h);
    }, []);

    const login = async (email, password) => {
        const res = await api.post("/api/auth/login", { email, password });
        setToken(res.token);
        setMe(res.user);
    };
    const logout = () => { setToken(null); setMe(null); };

    return (
        <Ctx.Provider value={{ me, loading, login, logout, user: me?.email || "", isAdmin: me?.role === "Admin" }}>
            {children}
        </Ctx.Provider>
    );
}

export const useUser = () => useContext(Ctx);