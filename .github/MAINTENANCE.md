# Maintenance workflows

The post-release and documentation workflows use a GitHub App to open pull requests
and trigger the required checks. To configure them:

1. [Register a GitHub App](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app)
   with repository permissions **Contents: read and write** and **Pull requests: read and write**.
   The app does not need a webhook.
2. Generate a private key for the app and install it on the UpdatR repository.
3. Add the app's client ID (not its numeric app ID) as the repository Actions variable
   `UPDATR_APP_CLIENT_ID`. Add the complete private-key PEM as the repository Actions
   secret `UPDATR_APP_PRIVATE_KEY`.
4. Enable **Allow auto-merge** in the repository settings. The app does not need to
   bypass the ruleset.
