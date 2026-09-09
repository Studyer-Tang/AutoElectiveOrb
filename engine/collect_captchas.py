"""Read-only batch image acquisition. Never imports the course-operation loop."""
import argparse
from pathlib import Path
import random
import sys
import time

def collect(fetch, directory, target, interval, report=print, sleep=time.sleep):
    from elective_orb_core.captcha.collection import save_image_pair
    from elective_orb_core.captcha.ttshitu import TTShituRecognizer
    if not 1 <= target <= 1000 or not 1 <= interval <= 3600:
        raise ValueError('invalid limits')
    added = duplicates = failures = 0
    for attempt in range(1, target * 3 + 1):
        response = None
        raw = bytearray()
        try:
            response = fetch()
            code = response.status_code
            if code in (401, 403, 429) or 300 <= code < 400:
                report('已停止：HTTP %s，登录失效、权限不足或限流。' % code)
                return added
            if 'text/html' in response.headers.get('Content-Type', '').lower():
                report('已停止：返回登录页或网页，而非验证码图片。')
                return added
            if code != 200:
                raise ValueError('http error')
            for chunk in response.iter_content(65536):
                raw.extend(chunk)
                if len(raw) > 3 * 1024 * 1024:
                    raise ValueError('image too large')
            encoded = TTShituRecognizer._encode_image(bytes(raw))  # Local conversion only, no recognizer instance.
            failures = 0
        except Exception:
            failures += 1
            report('请求或图片处理失败（连续 %s/3）；不输出响应及凭据。' % failures)
            if failures >= 3:
                report('连续失败，已停止。')
                return added
            if attempt < target * 3:
                sleep(interval)
            continue
        finally:
            if response is not None:
                response.close()
        try:
            if save_image_pair(directory, raw, encoded, ('batch-original', 'batch-preview')):
                added += 1
            else:
                duplicates += 1
        except Exception:
            report('本地保存失败或达到 5000 组 / 500 MB 上限，已停止。')
            return added
        report('新增 %s/%s · 重复 %s · 请求 %s/%s' % (added, target, duplicates, attempt, target * 3))
        if added >= target:
            report('目标完成。未上传 TT，未执行任何选退课操作。')
            return added
        if attempt < target * 3:
            sleep(interval)
    report('达到请求上限，已停止；可保留现有图片。')
    return added


def main():
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8', errors='replace')
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True)
    parser.add_argument('--count', type=int, default=300)
    parser.add_argument('--interval', type=int, default=2)
    args = parser.parse_args()
    if not 1 <= args.count <= 1000 or not 1 <= args.interval <= 3600:
        parser.error('count: 1–1000; interval: 1–3600 seconds')
    from elective_orb_core.environ import Environ
    Environ().config_ini = args.config
    from elective_orb_core.config import AutoElectiveConfig
    from elective_orb_core.const import ElectiveURL, USER_AGENT_LIST, DATA_DIR
    from elective_orb_core.iaaa import IAAAClient
    from elective_orb_core.elective import ElectiveClient
    from elective_orb_core.parser import get_sida
    config = AutoElectiveConfig()
    report = lambda message: print('COLLECT=' + message, flush=True)
    client = None
    try:
        report('正在登录；不会读取或操作目标课程。')
        agent = random.choice(USER_AGENT_LIST)
        iaaa = IAAAClient(timeout=20)
        iaaa.set_user_agent(agent)
        iaaa.oauth_home()
        login = iaaa.oauth_login(config.iaaa_id, config.iaaa_password)
        client = ElectiveClient(id='image-collection', timeout=15)
        client.set_user_agent(agent)
        response = client.sso_login(login.json()['token'])
        if config.is_dual_degree:
            client.sso_login_dual_degree(get_sida(response), config.identity, response.url)
        # Same session, URL, Referer and Rand convention as get_DrawServlet.
        # No response hooks: classify redirects and rate limits before reading the body.
        def fetch():
            return client._get(ElectiveURL.DrawServlet,
                params={'Rand': str(random.random() * 10000)},
                headers={'Referer': ElectiveURL.SupplyCancel, 'Cache-Control': 'no-cache'},
                allow_redirects=False, stream=True, timeout=(5, 15))
        collect(fetch, Path(DATA_DIR) / 'captcha-collection', args.count, args.interval, report)
        return 0
    except Exception:
        report('采集未完成：请检查登录、网络及本地磁盘；已保存图片保留。')
        return 1
    finally:
        if client is not None:
            client._session.close()


if __name__ == '__main__':
    sys.exit(main())
