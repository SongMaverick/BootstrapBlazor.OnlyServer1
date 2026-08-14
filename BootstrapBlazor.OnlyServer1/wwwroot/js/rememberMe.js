// (2026-08-14) 登录页「记住我」功能:把员工号 + 密码存到非 HttpOnly cookie(30 天),
//  下次访问登录页时自动填回表单 + 自动勾上 checkbox。
//
// 安全说明(项目里需要明确):
//   * 这两个 cookie 不是 HttpOnly,JS 才能读到;但 SameSite=Lax + 限定 path=/,
//     第三方站点发请求时不会带过来,XSS 仍是主要风险面。
//   * 密码以明文存 cookie 是用户明确要求的;生产环境如要加固,可以改成:
//       1) 服务端加密(对称密钥,放 DataProtection keys/ 里)
//       2) 或者只记用户名 + 浏览器自带密码管理器填密码(更安全,但用户体验差)
//     这里按用户当前要求保持明文,如需升级方案跟我提。

window.rememberMeInit = function (formId, userFieldId, passFieldId, rememberFieldId) {
    const form = document.getElementById(formId);
    const userField = document.getElementById(userFieldId);
    const passField = document.getElementById(passFieldId);
    const rememberField = document.getElementById(rememberFieldId);

    if (!form || !userField || !passField || !rememberField) return;

    const COOKIE_USER = 'remember_user';
    const COOKIE_PASS = 'remember_pass';
    const DAYS = 30;

    const getCookie = (name) => {
        const match = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'));
        return match ? decodeURIComponent(match[2]) : '';
    };
    const setCookie = (name, value, days) => {
        let expires = '';
        if (days) {
            const date = new Date();
            date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
            expires = '; expires=' + date.toUTCString();
        }
        document.cookie = name + '=' + encodeURIComponent(value) + expires + '; path=/; SameSite=Lax';
    };

    // 1) 页面加载:从 cookie 预填 + 自动勾上
    const savedUser = getCookie(COOKIE_USER);
    const savedPass = getCookie(COOKIE_PASS);
    if (savedUser) {
        userField.value = savedUser;
        passField.value = savedPass;
        rememberField.checked = true;
    }

    // 2) 提交表单前:根据 checkbox 状态写或清 cookie
    form.addEventListener('submit', function () {
        if (rememberField.checked) {
            setCookie(COOKIE_USER, userField.value, DAYS);
            setCookie(COOKIE_PASS, passField.value, DAYS);
        } else {
            setCookie(COOKIE_USER, '', -1);
            setCookie(COOKIE_PASS, '', -1);
        }
    });
};
