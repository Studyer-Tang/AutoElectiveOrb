"""Read credentials supplied by the native desktop shell."""

import os

IAAA_PASSWORD_ENV = "AUTOELECTIVE_IAAA_PASSWORD"


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
