# microk8s-discord-bot

```
# To Deploy:
docker-compose build
docker push 192.168.1.151:32000/microk8s-discord-bot:1.0.143
helm upgrade microk8s-discord-bot ./chart

# To access
kubectl port-forward -n kube-system service/kubernetes-dashboard 10443:443
```