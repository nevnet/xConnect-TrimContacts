# xConnect-TrimContacts
This scheduled Sitecore job can be configured to periodically remove contacts and interactions in xConnect older than X days.

This is immensely useful to prevent your xConnect shards from growing indefinitely. You can effectively cap the size of the shards or limit their growth rate, preventing the need to constantly re-shard or scale up/out the xDB shard databases.

The code includes some Unicorn .yml files that define a derived Command template. This Command template has an additional name/value field that allows specifying parameter values. The most important value to set is the "CutoffDays" parameter that controls how much data to delete from the shards. Any contacts (and their corresponding interactions) that have not ineracted with the site within the cutoff day period is a candidate for deletion.

You can use the built-in tasks scheduler to set this command to run as often as needed. Once per day is probably a reasonable frequency.

The command uses the new xConnect Data Tools API to send a request to xConnect. xConnect receives the request and registers the task with the Cortex Processing system. Cortex will then take care of fetching and deleting contacts in batches of ~500. The first time the command runs it may take a few hours (possibly days) to finish executing if there is a lot of data to be deleted. Subsequent execution should be fast as you're only deleting contacts that have fallen out of range since the last execution.

xConnect Data Tools API has an interesting calling pattern, where you must make the API request using a bearer token in the header, obtained from Identity Server using a Sitecore user that is a member of the "Sitecore XConnect Data Admin" role. You will need to create this user yourself, ensure it is the "Sitecore XConnect Data Admin" role, and specify the user name in the included config file. You will also need to include your Identity Server client secret in the included config file, obtainable from your Identity Server config files.

Everything this code does can also be done manually using Postman to call the xConnect Data Tools API. This might be useful if you only want to trim the contact & interaction data sporadically.
https://doc.sitecore.com/xp/en/developers/102/sitecore-experience-platform/web-api-for-xconnect-data-tools.html
