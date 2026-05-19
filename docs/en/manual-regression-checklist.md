# Manual Regression Checklist

[한국어](../OMNINODE_실환경_수동_최종회귀_체크리스트.md) · [English](./manual-regression-checklist.md)

Updated: 2026-05-19

Before release, manually check dashboard connection, chat, coding, mobile composer, routines, logic graphs, notebooks, plans, skills, Safe Refactor, settings, health endpoints, doctor, and Telegram.

Security checks: remote clients should enter limited mode without an OTP prompt, remote OTP requests should stay blocked, sensitive settings should remain blocked remotely, chat/coding/routine/logic graph execution should be blocked remotely, read-oriented views plus model/routing changes should stay available remotely, and unauthorized WebSocket requests should be rejected.
