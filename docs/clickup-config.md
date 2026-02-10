# ClickUp Configuration

This project uses ClickUp's API to pull time entries and task updates for a workspace.

## Access Requirements

- Use a ClickUp personal token with access to the target workspace.
- The token must be able to list workspaces and members.
- The token must be allowed to read time entries for all users in the workspace.

## Workspace ID

Configure the workspace (team) ID in the app config. This is the `id` value returned from the ClickUp `GET /team` API.

## Notes

- If time entries only show for the token owner, verify that the token belongs to an owner/admin or a role with permission to view all time entries.
- Some ClickUp workspaces require advanced time tracking permissions; ensure time tracking is enabled and visible for the workspace.
