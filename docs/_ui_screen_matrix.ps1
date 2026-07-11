$content = @'
---
project: PoRedoImage
diagram: stateDiagram-v2
last_updated: 2026-07-11
---
%% UI_Screen_Matrix.mmd — Client routes and screen states guarded by Blazor AuthorizeRouteView
%% Shows the AuthorizedRouteView plus AuthenticationStateProvider plus Layout flash mitigations loop.
%% Render with:
%%   npx @mermaid-js/mermaid-cli -i UI_Screen_Matrix.mmd -o UI_Screen_Matrix.svg

stateDiagram-v2
    direction TB

    [*]      --> Bootstrapping : App.razor mounts with WasmNoPrerender

    state Bootstrapping {
        [*] --> LoadingShell : skeleton plus Loading
        LoadingShell --> Deserializing : GetAuthenticationStateAsync
        Deserializing --> AuthKnown : AuthState populated claims only
        Deserializing --> AuthUnknown : no principal in cache
        AuthKnown --> Routing
        AuthUnknown --> Routing
    }

    Routing --> AuthorizedRoute : route in client features
    Routing --> AnonymousRoute : route is public login scalar

    state AuthorizedRoute {
        [*] --> AuthorizeCheck : AuthorizeRouteView
        AuthorizeCheck --> Page : user authenticated
        AuthorizeCheck --> RedirectToLogin : user anonymous
        RedirectToLogin --> LoginRedirect : redirect /login returnUrl
        LoginRedirect --> LoginState
        Page --> Loading : feature component InitializeAsync
        Loading --> Interactive : InteractiveWebAssembly hydrated
        Interactive --> Idle
        Idle --> Interactive : nav event
        Interactive --> Fetching : typed HttpClient plus CorrelationHeader
        Fetching --> Interactive : response
    }

    state LoginState {
        [*] --> RenderLogin : /login renders Login.razor
        RenderLogin --> PickProvider : user picks path
        PickProvider --> GUEST : dev only POST /auth/login/fake
        PickProvider --> Microsoft : /auth/login/microsoft OIDC
        GUEST --> CookieSet : 200 Set-Cookie HttpOnly SameSite Strict
        Microsoft --> EntraRedirect : 302 to login.microsoftonline.com
        EntraRedirect --> OidcCallback : /signin-oidc
        OidcCallback --> CookieSet : cookie plus claims back to WASM
        CookieSet --> PostLogin : AuthStateProvider NotifyAuthenticationStateChanged
        PostLogin --> AuthorizedRoute : returnUrl validated
    }

    state AnonymousRoute {
        [*] --> Render : RouteView renders page
        Render --> Idle
        Idle --> Render
    }

    state Reconnect {
        [*] --> Trying : connection lost
        Trying --> Reconnected : Blazor WASM reconnect
        Trying --> HardFail : more than 30 s
        HardFail --> Bootstrapping : reload
    }

    Interactive --> Reconnect : connection lost
    Idle --> LoggedOut : user taps /auth/logout
    LoggedOut --> Bootstrapping : new shell
'@
Set-Content -Path "UI_Screen_Matrix.mmd" -Value $content -NoNewline -Encoding UTF8
Write-Host "Wrote UI_Screen_Matrix.mmd ($(((Get-Content UI_Screen_Matrix.mmd).Count)) lines)"