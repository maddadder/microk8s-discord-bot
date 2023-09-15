# microk8s-discord-bot

## Inital Setup

1. Go to the Discord developer portal
2. Choose your bot
3. Go to the OAuth, and choose OAuth2 Generator
4. Choose Bot => Send Messages, Add Reactions
5. Copy/Paste URL into Browser, e.g. Click [Here](https://discord.com/api/oauth2/authorize?client_id=1149837019736981594&permissions=2112&scope=bot)
6. Choose your Server and click Next
7. Click Authorize

## Deployment
```
# To Deploy:
docker-compose build
docker push 192.168.1.151:32000/microk8s-discord-bot:1.0.168
helm upgrade microk8s-discord-bot ./chart

# To access
kubectl port-forward -n kube-system service/kubernetes-dashboard 10443:443
```