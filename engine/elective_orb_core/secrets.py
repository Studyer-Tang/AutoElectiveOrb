"""Read credentials supplied by the native desktop shell."""

import os

IAAA_PASSWORD_ENV = "AUTOELECTIVE_IAAA_PASSWORD"
TTSHITU_USERNAME_ENV = "AUTOELECTIVE_TT_USERNAME"
TTSHITU_PASSWORD_ENV = "AUTOELECTIVE_TT_PASSWORD"


class SecretStoreUnavailable(RuntimeError):
    pass


def get_password(kind, env_name):
    if env_name:
        value = os.environ.get(env_name)
        if value:
            return value
    raise SecretStoreUnavailable(
        "未找到 %s 凭据；请在桌面设置窗口中填写密码，或设置环境变量 %s"
        % (kind, env_name or "")
    )


def get_ttshitu_credentials():
    username = os.environ.get(TTSHITU_USERNAME_ENV, "").strip()
    password = os.environ.get(TTSHITU_PASSWORD_ENV, "")
    if username and password:
        return username, password
    raise SecretStoreUnavailable(
        "未找到 TT 识图凭据；请在桌面设置窗口中填写 TT 账号和密码，"
        "或设置环境变量 %s 与 %s"
        % (TTSHITU_USERNAME_ENV, TTSHITU_PASSWORD_ENV)
    )
