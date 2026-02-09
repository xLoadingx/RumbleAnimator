import requests

from json import loads, dumps
from os import environ
import subprocess

# this shit is messy asf but will probably be permenant :3

WEBHOOK_URL = environ["WEBHOOK_URL"]
GITHUB_CONTEXT = loads(environ["GITHUB_CONTEXT"])
REPO_CTX = GITHUB_CONTEXT["event"]["repository"]

EVENT_TYPE = GITHUB_CONTEXT["event_name"]

if EVENT_TYPE == "workflow_run":
    payload = None
    workflow_run = GITHUB_CONTEXT["event"]["workflow_run"]

    if workflow_run["conclusion"] == "failure":
        payload = dumps(
            {
                "content": "<@799732203289706546> fix this!!!",
                "tts": False,
                "embeds": [
                    {
                        "title": f'Build Failing for "{workflow_run["head_commit"]["message"]}"',
                        "url": workflow_run["html_url"],
                        "color": 16525609,
                    }
                ],
                "username": "Github Actions",
                "avatar_url": "https://cdn.discordapp.com/avatars/1462236419492810976/e57fd67dc7ca0cc840a0e87a82281bc5.webp",
            }
        )
        requests.post(
            WEBHOOK_URL, payload, headers={"Content-Type": "application/json"}
        )

    if workflow_run["conclusion"] == "success":
        subprocess.run(
            ["gh", "run", "download", str(workflow_run["id"]), "-n", "ReplayMod-Debug"]
        )
        payload = dumps(
            {
                "content": "",
                "tts": False,
                "embeds": [
                    {
                        "title": f'Build Passing for "{workflow_run["head_commit"]["message"]}"',
                        "url": workflow_run["html_url"],
                        "color": 38912,
                    }
                ],
                "username": "Github Actions",
                "avatar_url": "https://cdn.discordapp.com/avatars/1462236419492810976/e57fd67dc7ca0cc840a0e87a82281bc5.webp",
            }
        )
        files = {
            "file": ("ReplayMod-Debug.dll", open("ReplayMod.dll", "rb")),
        }
        requests.post(
            WEBHOOK_URL, payload, headers={"Content-Type": "application/json"}
        )
        requests.post(WEBHOOK_URL, files=files)


if EVENT_TYPE == "push":
    embed_title = f"[{REPO_CTX['name']}:{GITHUB_CONTEXT['ref_name']}] {len(GITHUB_CONTEXT['event']['commits'])} new commits"

    embed_description = ""
    for commit in GITHUB_CONTEXT["event"]["commits"]:
        embed_description += f" [`{commit['id'][:7]}`]({commit['url']}) {commit['message']} - {commit['author']['username']}\n"

    payload = dumps(
        {
            "content": "",
            "tts": False,
            "embeds": [
                {
                    "title": embed_title,
                    "url": REPO_CTX["html_url"],
                    "description": embed_description,
                    "color": 7506394,
                }
            ],
            "username": "Github Actions",
            "avatar_url": "https://cdn.discordapp.com/avatars/1462236419492810976/e57fd67dc7ca0cc840a0e87a82281bc5.webp",
        }
    )

    requests.post(WEBHOOK_URL, payload, headers={"Content-Type": "application/json"})
