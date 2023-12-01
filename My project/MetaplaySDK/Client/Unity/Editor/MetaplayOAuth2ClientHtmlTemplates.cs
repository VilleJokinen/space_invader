// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Text;

namespace Metaplay.Unity
{
    public static class MetaplayOAuth2ClientHtmlTemplates
    {

        static readonly string ResultHtmlTemplate =
@"
<!DOCTYPE html>
<html lang=""en"">

<head>
    <title>Metaplay</title>
    <style>
        @font-face {
            font-family: ministry;
            src: url(https://metaplay.io/assets/fonts/ministry_300.f915f361.woff2) format(""woff2""), url(https://metaplay.io//assets/fonts/ministry_300.c85311b6.woff) format(""woff""), url(https://metaplay.io//assets/fonts/ministry_300.eaa25d6c.otf) format(""opentype"");
            font-display: block;
            font-style: normal;
            font-weight: 300
        }

        @font-face {
            font-family: ministry;
            src: url(https://metaplay.io/assets/fonts/ministry_400.88b29018.woff2) format(""woff2""), url(https://metaplay.io//assets/fonts/ministry_400.c7f68eaf.woff) format(""woff""), url(https://metaplay.io//assets/fonts/ministry_400.ce55c404.otf) format(""opentype"");
            font-display: block;
            font-style: normal;
            font-weight: 400
        }

        @font-face {
            font-family: ministry;
            src: url(https://metaplay.io/assets/fonts/ministry_500.f7df56ba.woff2) format(""woff2""), url(https://metaplay.io//assets/fonts/ministry_500.0753a466.woff) format(""woff""), url(https://metaplay.io//assets/fonts/ministry_500.7ca148c8.otf) format(""opentype"");
            font-display: block;
            font-style: normal;
            font-weight: 500
        }

        @font-face {
            font-family: ministry;
            src: url(https://metaplay.io/assets/fonts/ministry_600.6b430545.woff2) format(""woff2""), url(https://metaplay.io//assets/fonts/ministry_600.1fb59958.woff) format(""woff""), url(https://metaplay.io//assets/fonts/ministry_600.b16923a2.otf) format(""opentype"");
            font-display: block;
            font-style: normal;
            font-weight: 600
        }

        html {
            font-family: sans-serif;
            line-height: 1.15;
            -webkit-text-size-adjust: 100%;
            -webkit-tap-highlight-color: rgba(0, 0, 0, 0)
        }

        body {
            color: #686868!important;
            margin: 0;
            font-family: Ministry, Avenir, Helvetica Neue, Arial, Noto Sans, sans-serif, Apple Color Emoji, Segoe UI Emoji, Segoe UI Symbol, Noto Color Emoji;
            font-size: 1rem;
            font-weight: 400;
            line-height: 1.5;
            color: #212529;
            text-align: left;
            background-color: #fff
        }

        h1 {
            letter-spacing: .02rem;
            text-transform: uppercase;
            font-weight: 700!important;
            margin-top: 0;
            margin-bottom: .5rem;
            font-size: 2.5rem;
            font-weight: 500;
            line-height: 1.2;
            color: #3b3b3b;
        }

        p {
            margin-top: 0;
            margin-bottom: 1rem
        }

        .navbar-brand {
            display: inline-block;
            padding-top: .3125rem;
            padding-bottom: .3125rem;
            margin-right: 1rem;
            font-size: 1.25rem;
            line-height: inherit;
            white-space: nowrap
        }

        .navbar-brand:focus,
        .navbar-brand:hover {
            text-decoration: none
        }

        .navbar {
            position: relative;
            padding: .5rem 1rem
        }

        .navbar,
        .navbar .container {
            display: flex;
            flex-wrap: wrap;
            align-items: center;
            justify-content: space-between
        }

        #prompt {
            padding-top: 7rem;
            padding-bottom: 7rem
        }

        @media(max-width:767px) {
            #prompt {
                padding-top: 4rem;
                padding-bottom: 0
            }
        }

        #header-logo {
            width: 150px;
            fill: #fff
        }

        #header-icon {
            width: 40px;
            fill: #fff;
            display: none
        }

        img,
        svg {
            vertical-align: middle
        }

        svg {
            overflow: hidden
        }

        .navbar a {
            color: #fff!important
        }

        .navbar-dark .navbar-brand,
        .navbar-dark .navbar-brand:focus,
        .navbar-dark .navbar-brand:hover {
            color: #fff
        }

        .navbar {
            background: #3b3b3b!important;
            padding-top: 0!important;
            padding-bottom: 0!important
        }

        .shadow {
            box-shadow: 0 .5rem 1rem rgba(0, 0, 0, .15)!important
        }

        @supports((position:-webkit-sticky) or (position:sticky)) {
            .sticky-top {
                position: -webkit-sticky;
                position: sticky;
                top: 0;
                z-index: 1020
            }
        }

        .bg-dark {
            background-color: #3b3b3b!important
        }

        @media(min-width:992px) {
            .navbar-expand-lg {
                flex-flow: row nowrap;
                justify-content: flex-start
            }
            .navbar-expand-lg .navbar-nav {
                flex-direction: row
            }
            .navbar-expand-lg .navbar-nav .dropdown-menu {
                position: absolute
            }
            .navbar-expand-lg .navbar-nav .nav-link {
                padding-right: .5rem;
                padding-left: .5rem
            }
            .navbar-expand-lg>.container {
                flex-wrap: nowrap
            }
            .navbar-expand-lg .navbar-nav-scroll {
                overflow: visible
            }
            .navbar-expand-lg .navbar-collapse {
                display: flex!important;
                flex-basis: auto
            }
            .navbar-expand-lg .navbar-toggler {
                display: none
            }
        }

        *,
         :after,
         :before {
            box-sizing: border-box;
        }

        @media (min-width: 1200px) {
            .container {
                max-width: 1140px;
            }
        }

        .container {
            width: 100%;
            padding-right: 15px;
            padding-left: 15px;
            margin-right: auto;
            margin-left: auto;
        }

        .row {
            display: flex;
            flex-wrap: wrap;
            margin-right: -15px;
            margin-left: -15px;
        }

        @media (min-width: 992px) {
            .col-lg-8 {
                flex: 0 0 66.66666667%;
                max-width: 66.66666667%;
            }
        }

        .col,
        .col-lg-8 {
            position: relative;
            width: 100%;
            padding-right: 15px;
            padding-left: 15px
        }

        .mb-0 {
            margin-bottom: 0!important
        }

        *,
         :after,
         :before {
            box-sizing: border-box
        }

        .text-success {
            color: #7bb72f!important
        }

        .mb-4 {
            margin-bottom: 1.5rem!important
        }

        .text-danger {
            color: #fa603f!important
        }

        a.text-danger:focus,
        a.text-danger:hover {
            color: #e62e06!important
        }
    </style>
</head>

<body>
    <div id=""app"">
        <nav class=""navbar shadow sticky-top navbar-dark bg-dark navbar-expand-lg"">
            <div class=""container"">
                <div class=""navbar-brand"">
                    <a href=""https://metaplay.io/"">
                        <svg viewBox=""0 0 2069 642"" xmlns=""http://www.w3.org/2000/svg"" fill-rule=""evenodd"" clip-rule=""evenodd"" stroke-linejoin=""round"" stroke-miterlimit=""2"" id=""header-logo"">
                            <path fill=""none"" d=""M.228 0h2068v641H.228z""></path>
                            <path d=""M1918.33 261.164l-38.214 37.714a9.048 9.048 0 01-6.982 3.088l-30.263-2.3c-3.482.1-5.264-4.148-2.759-6.57 28.152-27.222 47.278-33.29 74.208-37.968 3.626-.63 6.285 3.142 4.01 6.036m-.018 124.958a181.297 181.297 0 006.089-8.671c41.085-62.407 37.576-129.117 30.552-140.457-11.975-4.475-79.039-.641-131.16 46.135-23.898 23.369-39.528 48.072-49.255 68.259-3.784 8.399-6.262 15.37-7.604 19.859-1.073 3.586.268 7.699 3.391 10.396 7.413 6.402 65.06 53.68 72.789 59.698 3.256 2.534 7.552 3.045 10.859 1.29 4.277-2.268 10.901-6.226 18.723-11.865l12.366 34.363c1.159 3.474 5.69 4.31 8.013 1.479l31.398-38.285a12.783 12.783 0 002.425-11.554l-8.586-30.647zM1790.19 295.159l-13.64-1.042a12.785 12.785 0 00-10.856 4.64l-31.399 38.284c-2.322 2.832-.615 7.112 3.019 7.568l28.829 4.312c5.604-19.095 14.14-37.176 24.047-53.762M1810.95 438.642c-1.135-3.695-5.371-5.77-8.775-4.373l-26.662 11.48c-1.106.477-2.279-.524-1.981-1.692l7.089-27.809c.771-3.598-2.021-7.399-5.859-7.861-10.969-1.32-24.838-.474-32.463 8.795-4.908 5.968-7.183 13.053-8.163 20.628-2.272 17.574-9.542 35.566-15.938 42.503-1.203 1.305-.539 3.426 1.18 3.867 21.645 5.554 43.895 8.307 65.628 1.608 6.745-2.078 13.389-4.938 18.889-9.445 11.509-9.434 11.068-24.628 7.055-37.701M1666.04 329.711l.25 54.265c0 5.739-4.652 10.391-10.392 10.391h-52.422c-5.74 0-10.392-4.652-10.392-10.391v-56.221l-66.407-115.392c-3.988-6.926 1.012-15.573 9.006-15.573h52.994c4.017 0 7.675 2.316 9.393 5.948l28.824 60.947 28.525-60.912a10.39 10.39 0 019.41-5.983h56.005c8.074 0 13.063 8.806 8.914 15.732l-63.708 117.189zM822.043 284.297c-1.859-5.363-3.722-16.373-3.722-16.373s-1.86 11.01-3.41 16.373l-11.168 35.567h29.468l-11.168-35.567zM855.08 387.19l-6.979-21.596h-59.247l-6.98 21.596a10.394 10.394 0 01-9.889 7.196h-47.742c-7.371 0-12.398-7.461-9.632-14.293l71.614-176.813a10.392 10.392 0 019.631-6.49h45.242c4.233 0 8.043 2.568 9.632 6.49l71.612 176.813c2.767 6.832-2.261 14.293-9.632 14.293h-47.741a10.394 10.394 0 01-9.889-7.196M1161.68 383.993V207.181c0-5.739 4.652-10.391 10.392-10.391h46.838c5.74 0 10.393 4.652 10.393 10.391v132.442h66.846c5.739 0 10.391 4.653 10.391 10.392v33.978c0 5.739-4.652 10.393-10.391 10.393h-124.077c-5.74 0-10.392-4.654-10.392-10.393M1037.93 251.552h-12.098v31.051h12.098c12.407 0 19.542-3.952 19.542-15.526 0-11.291-6.205-15.525-19.542-15.525m11.787 84.119h-23.885v48.322c0 5.74-4.652 10.393-10.392 10.393h-46.839c-5.739 0-10.392-4.653-10.392-10.393V207.182c0-5.739 4.653-10.392 10.392-10.392h81.116c38.773 0 74.445 23.712 74.445 70.287 0 47.141-36.292 68.594-74.445 68.594M1434.95 284.297c-1.86-5.363-3.723-16.373-3.723-16.373s-1.86 11.01-3.41 16.373l-11.167 35.567h29.467l-11.167-35.567zm33.037 102.893l-6.979-21.596h-59.248l-6.98 21.596a10.392 10.392 0 01-9.888 7.196h-47.742c-7.371 0-12.399-7.461-9.632-14.293l71.613-176.813a10.393 10.393 0 019.632-6.49h45.242c4.232 0 8.043 2.568 9.632 6.49l71.612 176.813c2.767 6.832-2.261 14.293-9.632 14.293h-47.742a10.392 10.392 0 01-9.888-7.196M669.747 251.552v132.441c0 5.74-4.652 10.393-10.392 10.393h-46.838c-5.74 0-10.393-4.653-10.393-10.393V251.552h-38.62c-5.739 0-10.393-4.652-10.393-10.392v-33.978c0-5.739 4.654-10.392 10.393-10.392h144.861c5.739 0 10.393 4.653 10.393 10.392v33.978c0 5.74-4.654 10.392-10.393 10.392h-38.618zM379.726 383.993V207.181c0-5.739 4.652-10.391 10.392-10.391h126.559c5.739 0 10.392 4.652 10.392 10.391v33.979c0 5.739-4.653 10.392-10.392 10.392h-69.329v20.324h45.754c5.739 0 10.392 4.653 10.392 10.392v22.969c0 5.739-4.653 10.392-10.392 10.392h-45.754v23.994h79.564c5.74 0 10.392 4.653 10.392 10.392v33.978c0 5.739-4.652 10.393-10.392 10.393H390.118c-5.74 0-10.392-4.654-10.392-10.393M331.095 197.14h-44.47a10.391 10.391 0 00-8.701 4.712l-46.867 71.787-34.733-54.907a1.595 1.595 0 00-2.407-.328c-43.911 39.525-61.553 81.347-68.284 103.567-2.532 8.359-3.765 17.039-3.765 25.773v36.611c0 5.739 4.654 10.382 10.393 10.382H179.1c5.739 0 10.391-4.654 10.391-10.393v-78.808l32.454 49.446c4.052 6.175 13.072 6.269 17.254.181l34.666-50.473v79.654c0 5.739 4.652 10.393 10.391 10.393h46.839c5.739 0 10.393-4.654 10.393-10.393V207.533c0-5.74-4.654-10.393-10.393-10.393M172.595 197.14h-40.334c-5.739 0-10.393 4.653-10.393 10.393v40.063c0 1.663 2.059 2.427 3.16 1.179 17.922-20.324 35.375-37.043 48.629-48.892 1.08-.966.386-2.743-1.062-2.743""></path>
                        </svg>
                        <svg viewBox=""0 0 1025 1025"" xmlns=""http://www.w3.org/2000/svg"" fill-rule=""evenodd"" clip-rule=""evenodd"" stroke-linejoin=""round"" stroke-miterlimit=""2"" id=""header-icon"">
                            <path fill=""none"" d=""M.139 0h1024v1024H.139z""></path>
                            <path d=""M752.936 179.706l-122.961 121.35a29.108 29.108 0 01-22.466 9.938l-97.376-7.402c-11.205.323-16.937-13.348-8.879-21.139 90.584-87.592 152.128-107.116 238.778-122.17 11.669-2.028 20.223 10.11 12.904 19.423m-.06 402.075c6.693-8.983 13.251-18.264 19.595-27.902 132.197-200.804 120.906-415.456 98.306-451.945-38.533-14.398-254.322-2.061-422.03 148.448-76.895 75.194-127.188 154.681-158.487 219.637-12.174 27.024-20.148 49.453-24.467 63.897-3.454 11.54.863 24.775 10.911 33.452 23.853 20.598 209.343 172.726 234.212 192.087 10.477 8.156 24.299 9.798 34.941 4.153 13.76-7.297 35.076-20.033 60.242-38.178L645.891 836c3.73 11.177 18.308 13.867 25.781 4.756l101.031-123.187a41.127 41.127 0 007.802-37.176l-27.629-98.612zM340.607 289.089l-43.888-3.35a41.129 41.129 0 00-34.931 14.928L160.756 423.855c-7.472 9.11-1.979 22.882 9.712 24.35l92.762 13.874c18.034-61.44 45.5-119.619 77.377-172.99M407.414 750.773c-3.65-11.891-17.28-18.568-28.235-14.073l-85.788 36.941c-3.56 1.534-7.332-1.688-6.374-5.444l22.809-89.48c2.483-11.578-6.504-23.81-18.853-25.296-35.292-4.246-79.921-1.524-104.453 28.3-15.795 19.203-23.115 41.999-26.267 66.373-7.311 56.549-30.702 114.44-51.282 136.762-3.871 4.198-1.737 11.023 3.795 12.442 69.647 17.871 141.241 26.73 211.17 5.176 21.705-6.689 43.082-15.892 60.778-30.394 37.033-30.355 35.613-79.244 22.7-121.307""></path>
                        </svg>
                    </a>
                </div>
            </div>
        </nav>
        <div style=""min-height:70vh;"">
            <div id=""prompt"">
                <div class=""container"">
                    <div class=""row"">
                        <div class=""col-lg-8"">
                            <h1 class=""mb-0"">Metaplay Unity Editor login</h1>
                            <h1 class=""mb-4 {{classname}}"">{{title}}</h1>
                            <p>{{content}}</p>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>

</html>
";

        public static byte[] RenderSuccess()
        {
            return Render(className: "text-success", title: "Login succeeded", text: "You have logged in successfully. You can close this window now and return to Unity Editor.");
        }

        public static byte[] RenderFailure(string message)
        {
            return Render(className: "text-danger", title: "Login Failed", text: message);
        }

        static byte[] Render(string className, string title, string text)
        {
            return Encoding.UTF8.GetBytes(ResultHtmlTemplate
                .Replace("{{classname}}", className)
                .Replace("{{title}}", EscapeForTextNode(title))
                .Replace("{{content}}", EscapeForTextNode(text)));
        }

        static string EscapeForTextNode(string raw)
        {
            return raw.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
